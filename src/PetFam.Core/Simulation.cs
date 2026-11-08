using PetFam.Core.Behavior;
using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Interactions;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.Social;
using PetFam.Core.World;

namespace PetFam.Core;

/// <summary>
/// Ticks every pet: needs, bond, appearance, behavior. Polls the platform for user context at 10 Hz
/// (cursor every tick, since hover/look needs it). Rendering is someone else's job; this class never touches a window.
/// </summary>
public sealed class Simulation
{
    public const float UserPollInterval = 0.1f;
    public const float LookDistance = 340f;
    public const float StrokeInterval = 0.5f;
    public const float TurnSpeedDegPerSec = 480f;

    private readonly IPlatform _platform;
    private readonly Random _rng;
    private readonly Dictionary<Guid, float> _lastStroke = new();
    private float _sincePoll = UserPollInterval;
    private float _time;

    public World.World World { get; } = new();
    public List<Pet> Pets { get; } = new();
    public UserContext User { get; } = new();
    public DebugOptions Debug { get; } = new();

    /// <summary>Friendship and pecking order between pets.</summary>
    public SocialState Social { get; } = new();

    public SocialDirector Director { get; } = new();

    // ---------------------------------------------------------------- weather

    /// <summary>The real weather (set by the app from a weather service, or a test override).</summary>
    public WeatherNow Weather { get; private set; } = WeatherNow.Clear;

    /// <summary>0 no snow on the ground … 1 a white blanket (builds up while it snows, melts afterwards).</summary>
    public float SnowCover { get; private set; }

    /// <summary>Seconds since the last lightning strike (for the flash).</summary>
    public float SinceLightning { get; private set; } = 999f;

    private float _nextLightning = 8f;

    /// <summary>New weather. When it turns, pets on screen say something about it.</summary>
    public void SetWeather(WeatherNow weather)
    {
        var before = Weather;
        Weather = weather;
        if (before.Kind == weather.Kind) return;
        var line = (weather.Kind, before.Kind) switch
        {
            (WeatherKind.Rain, _) => (Cat: L.S("Rain… I hate it", "비 온다… 싫어"), Dog: L.S("Rain!", "비다!")),
            (WeatherKind.Thunder, _) => (Cat: L.S("Rumble… scary", "우르릉… 무서워"), Dog: L.S("Woof! Thunder!", "왈! 천둥이다")),
            (WeatherKind.Snow, _) => (Cat: L.S("Cold stuff is falling", "차가운 게 내려와"), Dog: L.S("Snow! Woof woof!", "눈이다! 멍멍!")),
            (WeatherKind.Clear, WeatherKind.Rain or WeatherKind.Thunder or WeatherKind.Snow) => (Cat: L.S("It stopped, meow", "그쳤다냥"), Dog: L.S("It stopped! Let's play!", "그쳤다! 놀자!")),
            _ => (Cat: "", Dog: ""),
        };
        foreach (var pet in Pets.Where(p => IsVisible(p) && !p.Hidden).Take(2))
        {
            if (weather.Raining) { pet.SayAs(v => v.Rain, _rng, 2.6f); continue; }
            if (weather.Snowing) { pet.SayAs(v => v.Snow, _rng, 2.6f); continue; }
            var text = pet.Species == PetSpecies.Cat ? line.Cat : line.Dog;
            if (text.Length > 0) pet.Say(text, 2.6f);
        }
    }

    /// <summary>Test: snow already lying (to see snowy roofs without waiting).</summary>
    public void DebugSnowCover(float cover) => SnowCover = System.Math.Clamp(cover, 0f, 1f);

    private void TickWeather(float dt)
    {
        var w = Weather;
        SnowCover = w.Snowing
            ? System.Math.Min(1f, SnowCover + dt * w.Intensity / 600f)
            : System.Math.Max(0f, SnowCover - dt / (w.TemperatureC > 2f ? 900f : 3600f));

        SinceLightning += dt;
        if (w.Kind == WeatherKind.Thunder)
        {
            _nextLightning -= dt;
            if (_nextLightning <= 0f)
            {
                _nextLightning = 6f + (float)_rng.NextDouble() * 18f / System.Math.Max(0.4f, w.Intensity);
                SinceLightning = 0f;
                foreach (var pet in Pets.Where(p => IsVisible(p) && !p.Hidden && _rng.NextDouble() < 0.6))
                    pet.Say(pet.Species == PetSpecies.Cat ? L.S("Eek!", "히익!") : L.S("Wuf!", "왕!"), 1.2f);
            }
        }
    }

    /// <summary>Out under the sky: not inside a house, not under cover in the wallpaper.</summary>
    public static bool Exposed(Pet pet, Cell cell) =>
        !pet.Hidden && pet.Layer != PetLayer.WindowEdge && cell.Map.ZoneAt(pet.Position, ZoneType.Hide) is null;

    // ---------------------------------------------------------------- day and night

    /// <summary>Sunrise and sunset at the user's place, and the computer's clock.</summary>
    public DayClock Clock { get; } = new();

    /// <summary>0 at night … 1 in full day (twilight in between). Updated every tick.</summary>
    public float Daylight { get; private set; } = 1f;

    public DayPhase Phase { get; private set; } = DayPhase.Day;

    /// <summary>Moves pets between virtual desktops (plan §6.4).</summary>
    public TravelPlanner Travel { get; } = new();

    /// <summary>The cell (virtual desktop) the user is looking at. Null: everything is visible (single cell, tests).</summary>
    public string? VisibleCellId { get; private set; }

    public bool IsVisible(Pet pet) => VisibleCellId is null || pet.CellId == VisibleCellId;

    /// <summary>
    /// The user is now looking at this cell (switched virtual desktop). Pets left on the other desktops let go of
    /// anything screen-bound (held by the mouse, peeking at a window that is no longer on screen); follow-mode pets
    /// start coming over a moment later.
    /// </summary>
    public void ShowCell(Cell cell, DateTime nowUtc)
    {
        if (VisibleCellId == cell.Id) return;
        var first = VisibleCellId is null;
        VisibleCellId = cell.Id;
        foreach (var pet in Pets)
        {
            if (pet.CellId == cell.Id) continue;
            var own = World.Find(pet.CellId) ?? cell;
            if (pet.Hold != HoldState.None || pet.Layer != PetLayer.Backyard || pet.Peek is not null)
            {
                pet.Hold = HoldState.None;
                pet.Engine.ForceStop(new BehaviorContext
                {
                    Pet = pet, Cell = own, User = User, Rng = _rng, Debug = Debug, NowUtc = nowUtc, Visible = false,
                });
                pet.Peek = null;
                pet.Layer = PetLayer.Backyard;
                pet.Position = own.Map.ClampToRoam(pet.Position);
            }
            pet.LookAt = null;
        }
        if (!first) Travel.OnSwitched(Pets, _rng);
    }

    // ---------------------------------------------------------------- chatter (each breed's way of talking)

    private readonly Dictionary<Guid, float> _nextChat = new();

    /// <summary>Now and then a pet on screen says something in its breed's voice: what it needs, or what it is like.</summary>
    private void TickChatter(float dt)
    {
        foreach (var pet in Pets)
        {
            if (!_nextChat.TryGetValue(pet.Id, out var left)) left = 20f + (float)_rng.NextDouble() * 40f;
            left -= dt;
            if (left <= 0f)
            {
                // Talkative breeds (playful, outgoing) talk more often; shy ones rarely.
                var chatty = 0.4f + pet.Personality.Playfulness * 0.8f - pet.Personality.Shyness * 0.5f;
                left = (50f + (float)_rng.NextDouble() * 90f) / System.Math.Max(0.25f, chatty);
                if (IsVisible(pet) && !pet.Hidden && pet.Bubble is null && pet.Layer == PetLayer.Backyard &&
                    pet.Hold == HoldState.None && pet.Session is null && pet.Animation is not ("sleep" or "doze"))
                {
                    var n = pet.Needs;
                    pet.SayAs(n.Hunger < 30f ? v => v.Hungry
                        : n.Energy < pet.Traits.SleepThreshold + 5f ? v => v.Sleepy
                        : n.Fun > 75f && _rng.NextDouble() < 0.4 ? v => v.Happy
                        : v => v.Idle, _rng, 2.8f);
                }
            }
            _nextChat[pet.Id] = left;
        }
    }

    /// <summary>Tray: call a pet on another desktop over to this one.</summary>
    public bool CallPet(Pet pet) => TravelPlanner.Call(this, pet, _rng);

    private Vec2 _lastCursor;
    private bool _hasCursor;

    public Simulation(IPlatform platform, Random? rng = null)
    {
        _platform = platform;
        _rng = rng ?? new Random();
    }

    public Pet AddPet(PetSpecies species, string name, Cell cell, DateTime nowUtc) => AddPet(Breed.Default(species), name, cell, nowUtc);

    public Pet AddPet(Breed breed, string name, Cell cell, DateTime nowUtc)
    {
        var pet = new Pet(breed.Species, name, nowUtc, _rng, breed)
        {
            CellId = cell.Id,
            Position = cell.Map.RoamArea.Center,
        };
        // Spike: start curious so the first peek happens within a minute or two.
        pet.Needs.Curiosity = 60f;
        Pets.Add(pet);
        return pet;
    }

    public void Tick(float dt, DateTime nowUtc)
    {
        if (dt <= 0f) return;
        dt = System.Math.Min(dt, 0.25f); // never let a hitch teleport pets
        _time += dt;

        User.Cursor = _platform.GetCursorPosition();
        TrackCursor(dt);
        _sincePoll += dt;
        if (_sincePoll >= UserPollInterval)
        {
            _sincePoll = 0f;
            PollUser();
        }

        // Day and night follow the computer's clock and the real sunrise and sunset where the user is.
        Daylight = Clock.Daylight(nowUtc);
        Phase = Clock.Phase(nowUtc);
        var isDaytime = Daylight >= 0.5f;

        Director.Tick(Pets, p => World.Find(p.CellId) ?? World.AnyCell(), Social, _rng, nowUtc, dt);

        foreach (var c in World.Cells) c.Ball?.Step(dt, c.Map);
        Travel.Tick(this, dt, _rng);
        TickWeather(dt);
        TickChatter(dt);

        foreach (var pet in Pets)
        {
            var cell = World.Find(pet.CellId) ?? World.AnyCell();
            if (cell is null) continue;

            var sleeping = pet.Engine.Current is SleepBehavior;
            pet.Needs.Tick(dt, User.IsActive, sleeping, pet.Traits);
            var grew = pet.Growth.Tick(dt, User.IsActive, DateOnly.FromDateTime(nowUtc.ToLocalTime()));
            if (grew is Growth.Event.GrewUp or Growth.Event.Anniversary) Celebrate(pet, grew);
            pet.Bond.Tick(nowUtc, User.IsActive);

            var inSun = cell.Map.ZoneAt(pet.Position, ZoneType.Sun) is not null && Weather.Kind is WeatherKind.Clear;
            if (Weather.Raining || Weather.Snowing)
            {
                if (Exposed(pet, cell))
                    pet.Appearance.Rain(dt * Weather.Intensity / (Weather.Snowing ? 900f : 120f));
                if (Weather.Snowing && pet.Species == PetSpecies.Dog && Exposed(pet, cell))
                    pet.Needs.Fun = System.Math.Min(100f, pet.Needs.Fun + dt * 0.4f * pet.Breed.SnowLove);
            }
            pet.Appearance.Tick(dt, isDaytime, inSun);

            var visible = IsVisible(pet);
            if (visible) UpdateLook(pet, sleeping);

            var ctx = new BehaviorContext
            {
                Pet = pet,
                Cell = cell,
                User = User,
                Rng = _rng,
                Debug = Debug,
                NowUtc = nowUtc,
                IsDaytime = isDaytime,
                Pets = Pets,
                Social = Social,
                Visible = visible,
                Weather = Weather,
            };

            foreach (var b in pet.Engine.Behaviors)
            {
                if (b is PeekBehavior peek) peek.TickCooldown(dt);
                if (b is SwimBehavior swim) swim.TickCooldown(dt);
                if (b is HuntCursorBehavior hunt) hunt.TickCooldown(dt);
                if (b is ShelterBehavior shelter) shelter.TickCooldown(dt);
                if (b is CooldownBehavior cooling) cooling.TickCooldown(dt);
            }

            pet.Engine.Update(ctx, dt);
            pet.AnimationTime += dt;
            pet.TickBubble(dt);
            TurnToward(pet, dt);
            pet.Motion.EndFrame(pet, dt, cell.Map.DepthScale(pet.Position.Y));
        }
    }

    private void TrackCursor(float dt)
    {
        var c = User.Cursor;
        if (!_hasCursor) { _lastCursor = c; _hasCursor = true; }
        var moved = Vec2.Distance(c, _lastCursor);
        _lastCursor = c;
        var instant = moved / System.Math.Max(dt, 1e-3f);
        User.CursorSpeed += (instant - User.CursorSpeed) * System.Math.Min(1f, dt * 6f);
        User.CursorStill = moved > 1.5f ? 0f : User.CursorStill + dt;
    }

    // --- pets joining and social play ---

    private static string[] CatNames => L.Ko ? new[] { "나비", "치즈", "루루", "보리", "까미" } : new[] { "Kitty", "Mittens", "Ginger", "Oreo", "Shadow" };
    private static string[] DogNames => L.Ko ? new[] { "초코", "두부", "몽이", "해피", "별이" } : new[] { "Buddy", "Max", "Bella", "Charlie", "Daisy" };
    public const int MaxPets = 6;

    /// <summary>
    /// Says goodbye to a pet for good: ends its play session (the partner goes back to normal), drops its
    /// relations, a ball game it was running and everything else that pointed at it.
    /// </summary>
    public bool RemovePet(Pet pet)
    {
        if (!Pets.Contains(pet)) return false;
        pet.Session?.Abort();
        if (World.Find(pet.CellId) is { } cell && pet.Queue.Any(q => q.Interaction.Id == "ball")) cell.Ball = null;
        foreach (var c in World.Cells)
            if (c.Ball?.CarriedBy == pet.Id) c.Ball = null;
        pet.Queue.Clear();
        Pets.Remove(pet);
        // Its house now belongs to everybody.
        foreach (var c in World.Cells)
            foreach (var h in c.Houses.Where(h => h.OwnerId == pet.Id)) h.OwnerId = null;
        Social.Forget(pet.Id);
        _lastStroke.Remove(pet.Id);
        return true;
    }

    /// <summary>Builds a house in the cell; false when the cell already has the maximum number.</summary>
    public bool AddHouse(Cell cell, PetHouse house)
    {
        if (cell.Houses.Count >= Cell.MaxHouses) return false;
        house.Position = cell.Map.ClampToRoam(house.Position);
        cell.Houses.Add(house);
        return true;
    }

    /// <summary>Tears a house down; pets sleeping inside come out.</summary>
    public void RemoveHouse(Cell cell, PetHouse house)
    {
        if (!cell.Houses.Remove(house)) return;
        foreach (var pet in Pets.Where(p => p.Hidden && p.CellId == cell.Id))
            if (pet.InsideHouse == house.Id || Vec2.Distance(pet.Position, house.Position) < 40f)
            {
                pet.Hidden = false;
                pet.InsideHouse = null;
            }
        foreach (var pet in Pets.Where(p => p.Doorway?.HouseId == house.Id)) pet.Doorway = null;
    }

    /// <summary>A free floor spot for a new house: near the bottom middle, away from the other houses.</summary>
    public static Vec2 SpotForNewHouse(Cell cell)
    {
        var roam = cell.Map.RoamArea;
        var y = roam.Bottom - roam.Height * 0.3f;
        for (var i = 0; i < 12; i++)
        {
            var x = roam.X + roam.Width * (0.5f + (i % 2 == 0 ? 1 : -1) * 0.12f * ((i + 1) / 2));
            var p = cell.Map.ClampToRoam(new Vec2(x, y));
            var depth = cell.Map.DepthScale(p.Y);
            if (cell.Houses.All(h => MathF.Abs(h.Position.X - p.X) > PetHouse.BaseWidth * depth * 1.2f || MathF.Abs(h.Position.Y - p.Y) > 60f))
                return p;
        }
        return cell.Map.ClampToRoam(new Vec2(roam.X + roam.Width / 2f, y));
    }

    /// <summary>A pet grew into a new stage or reached an anniversary: a speech bubble and confetti.</summary>
    public static void Celebrate(Pet pet, Growth.Event e)
    {
        pet.Celebration = e;
        if (e == Growth.Event.GrewUp)
            pet.Say(pet.Stage switch
            {
                LifeStage.Junior => L.S($"{pet.Name} is a junior now!", $"{pet.Name}, 청소년이 됐어요!"),
                LifeStage.Adult => L.S($"{pet.Name} is all grown up!", $"{pet.Name}, 다 컸어요!"),
                LifeStage.Senior => L.S($"{pet.Name} is a dignified senior now", $"{pet.Name}, 의젓한 어르신이 됐어요"),
                _ => $"{pet.Name}",
            }, 5f);
        else if (e == Growth.Event.Anniversary)
            pet.Say(L.S($"{pet.Growth.Days} days together!", $"함께한 지 {pet.Growth.Days}일!"), 5f);
    }

    /// <summary>Adds another pet with a fresh name at a random spot on the floor. Null when full.</summary>
    public Pet? AdoptPet(PetSpecies species, Cell cell, DateTime nowUtc) => AdoptPet(Breed.Default(species), cell, nowUtc);

    /// <summary>A new baby of this breed, with a name that suits it (and is not taken yet). Null when full.</summary>
    public Pet? AdoptPet(Breed breed, Cell cell, DateTime nowUtc)
    {
        if (Pets.Count >= MaxPets) return null;
        var used = Pets.Select(p => p.Name).ToHashSet();
        var pool = breed.Names.Select(n => n.Text).Concat(breed.Species == PetSpecies.Cat ? CatNames : DogNames);
        var name = pool.FirstOrDefault(n => !used.Contains(n)) ?? $"{breed.Name} {Pets.Count + 1}";
        var pet = AddPet(breed, name, cell, nowUtc);
        pet.Position = cell.Map.RandomPoint(_rng);
        return pet;
    }

    /// <summary>Starts an encounter between two pets right away (test option); false if either is busy.</summary>
    public bool StartPlay(Pet a, Pet b)
    {
        if (!SocialDirector.Available(a) || !SocialDirector.Available(b)) return false;
        Director.Start(SocialDirector.Choose(a, b, Social, _rng));
        return true;
    }

    /// <summary>Rotate the body smoothly toward TargetHeading and keep the 2D Facing in sync.</summary>
    public static void TurnToward(Pet pet, float dt)
    {
        var diff = NormalizeDeg(pet.TargetHeading - pet.Heading);
        // Ease out: fast through most of the turn, settling gently onto the new heading.
        var step = MathF.Min(TurnSpeedDegPerSec, 140f + MathF.Abs(diff) * 6f) * dt;
        pet.Heading = MathF.Abs(diff) <= step ? pet.TargetHeading : pet.Heading + MathF.Sign(diff) * step;
        pet.Heading = NormalizeDeg(pet.Heading);
        var c = MathF.Cos(pet.Heading * MathF.PI / 180f);
        if (MathF.Abs(c) > 0.05f) pet.Facing = c >= 0 ? 1f : -1f;
    }

    public static float NormalizeDeg(float deg)
    {
        deg %= 360f;
        if (deg > 180f) deg -= 360f;
        if (deg <= -180f) deg += 360f;
        return deg;
    }

    // --- interactions (plan §7.1) ---

    /// <summary>Puts a food bowl down in front of the pet. The pet walks over and eats when hungry enough.</summary>
    public FoodBowl Feed(Pet pet, DateTime nowUtc)
    {
        var cell = World.Find(pet.CellId) ?? World.AnyCell()!;
        var spot = cell.Map.ClampToRoam(pet.Position + new Vec2(pet.Facing * 140f, 0f));
        return PlaceBowl(cell, spot);
    }

    public FoodBowl PlaceBowl(Cell cell, Vec2 position)
    {
        var bowl = new FoodBowl { Position = cell.Map.ClampToRoam(position), Amount = 1f };
        cell.Bowls.Add(bowl);
        return bowl;
    }

    public bool Stroke(Pet pet, DateTime nowUtc)
    {
        pet.Needs.ReceiveAffection();
        var counted = pet.Bond.Pet(nowUtc);
        if (pet.Bubble is null && _rng.NextDouble() < 0.35) pet.SayAs(v => v.Petted, _rng);
        return counted;
    }

    public void Towel(Pet pet, DateTime nowUtc)
    {
        if (pet.Appearance.IsClean) return;
        pet.Appearance.Towel();
        pet.Bond.Wash(nowUtc);
    }

    // --- mouse holds (driven by the host's front window) ---

    public void BeginCarry(Pet pet)
    {
        // A Shiba screams, a Ragdoll goes limp, a Pomeranian enjoys the view.
        pet.SayAs(v => v.Held, _rng, 2f);
        pet.Hold = HoldState.Carried;
        pet.LookAt = null;
        pet.TargetHeading = 90f;   // dangling pets face the person holding them
    }

    /// <summary>Move the carried pet so its scruff follows the cursor. grabOffset is renderer-defined (feet → scruff, facing-local).</summary>
    public void UpdateCarry(Pet pet, Vec2 cursor, Vec2 grabOffset)
    {
        if (pet.Hold != HoldState.Carried) return;
        pet.Position = new Vec2(cursor.X - grabOffset.X * pet.Facing, cursor.Y - grabOffset.Y);
    }

    public void EndCarry(Pet pet, Vec2 dropFeet)
    {
        if (pet.Hold != HoldState.Carried) return;
        var cell = World.Find(pet.CellId) ?? World.AnyCell()!;
        pet.Position = cell.Map.ClampToRoam(dropFeet);
        pet.Hold = HoldState.None;
    }

    public void BeginStroke(Pet pet) => pet.Hold = HoldState.Petted;

    // --- ball held and thrown by the person (driven by the host's ball window) ---

    public void BeginBallHold(Ball ball)
    {
        ball.HeldByUser = true;
        ball.Velocity = Vec2.Zero;
        ball.VerticalSpeed = 0f;
    }

    /// <summary>The ball follows the cursor; above the roam area it hangs in the air over the nearest ground point.</summary>
    public void UpdateBallHold(Cell cell, Ball ball, Vec2 cursor)
    {
        if (!ball.HeldByUser) return;
        var ground = cell.Map.ClampToRoam(cursor);
        var depth = cell.Map.DepthScale(ground.Y);
        ball.Position = ground;
        ball.Height = System.Math.Max(0f, (ground.Y - cursor.Y) / (depth * 0.91f));
    }

    public void ThrowBall(Ball ball, Vec2 flick)
    {
        if (!ball.HeldByUser) return;
        ball.Throw(flick);
    }

    // --- Sims-style interactions ---

    /// <summary>What the pie menu should show for this pet right now.</summary>
    public IReadOnlyList<(Interaction Interaction, Availability Availability)> MenuFor(Pet pet, DateTime nowUtc)
    {
        var cell = World.Find(pet.CellId) ?? World.AnyCell()!;
        return InteractionCatalog.All
            .Select(i => (i, i.Check(pet, cell, nowUtc)))
            .Where(x => x.Item2.Visible)
            .ToList();
    }

    /// <summary>Adds an interaction to the pet's queue. False when unavailable or the queue is full.</summary>
    public bool Enqueue(Pet pet, Interaction interaction, DateTime nowUtc)
    {
        var cell = World.Find(pet.CellId) ?? World.AnyCell()!;
        if (pet.Queue.Count >= Pet.MaxQueue) return false;
        if (!interaction.Check(pet, cell, nowUtc).Enabled) return false;
        pet.Queue.Add(new QueuedAction { Interaction = interaction });
        return true;
    }

    /// <summary>Cancels a queued or running action (clicking its chip in the queue).</summary>
    public void Cancel(Pet pet, QueuedAction action)
    {
        if (action.Status == QueueStatus.Running) action.CancelRequested = true;
        else pet.Queue.Remove(action);
    }

    public void BeginAttention(Pet pet)
    {
        if (pet.Hold == HoldState.None) pet.Hold = HoldState.Attention;
    }

    public void EndAttention(Pet pet)
    {
        if (pet.Hold == HoldState.Attention) pet.Hold = HoldState.None;
    }

    public const float PatSeconds = 1.6f;

    /// <summary>A single click: one stroke, and the pet stops (even mid-walk) to enjoy it for a moment.</summary>
    public bool Pat(Pet pet, DateTime nowUtc)
    {
        if (pet.Hold == HoldState.Carried) return false;
        pet.PatTimer = PatSeconds;
        return Stroke(pet, nowUtc);
    }

    /// <summary>Called while the mouse moves over a petted pet; rate-limited so a long stroke counts as a few pats.</summary>
    public bool StrokeMove(Pet pet, DateTime nowUtc)
    {
        if (pet.Hold != HoldState.Petted) return false;
        if (_lastStroke.TryGetValue(pet.Id, out var last) && _time - last < StrokeInterval) return false;
        _lastStroke[pet.Id] = _time;
        return Stroke(pet, nowUtc);
    }

    public void EndStroke(Pet pet)
    {
        if (pet.Hold == HoldState.Petted) pet.Hold = HoldState.None;
    }

    private void UpdateLook(Pet pet, bool sleeping)
    {
        if (sleeping || pet.Hold == HoldState.Carried)
        {
            pet.LookAt = null;
            return;
        }
        if (!User.CursorOnDesktop)
        {
            pet.LookAt = pet.Gaze;
            return;
        }

        var near = Vec2.Distance(User.Cursor, pet.Position) <= LookDistance;
        pet.LookAt = near ? User.Cursor : pet.Gaze;

        // If the cursor is behind the pet (beyond what the neck can reach), turn the body toward it while idle.
        if (near && pet.Engine.Current is IdleBehavior or SitBehavior or LieBehavior)
        {
            var toCursor = User.Cursor - pet.Position;
            var angle = MathF.Atan2(toCursor.Y, toCursor.X) * 180f / MathF.PI;
            if (MathF.Abs(NormalizeDeg(angle - pet.Heading)) > 95f)
                pet.TargetHeading = angle;
        }
    }

    private void PollUser()
    {
        User.Foreground = _platform.GetForegroundWindow();
        User.Idle = _platform.GetIdleTime();
        User.DoNotDisturb = _platform.IsDoNotDisturb();
        User.CursorOnDesktop = _platform.IsDesktopAt(User.Cursor);
    }
}
