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

    /// <summary>Pets chatting with each other in their own personas, written by the user's AI (if there is one).</summary>
    public Talk.TalkDirector Talk { get; } = new();

    /// <summary>What each pet did today, and the diaries and stories written from it.</summary>
    public Talk.Journal Journal { get; } = new();

    private DateTime _now = DateTime.UtcNow;

    /// <summary>The time of the last tick.</summary>
    public DateTime Now => _now;
    private readonly Dictionary<Guid, string> _lastDoing = new();
    private readonly Dictionary<Guid, Social.SocialSession?> _lastSession = new();
    private float _sinceWeatherNote = 999f;
    private bool _noteWeather;

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

    /// <summary>
    /// The user's main cell: the current virtual desktop on the main monitor. Null: everything is visible (single
    /// cell, tests). The other monitors of the same desktop are on screen too (<see cref="VisibleCellIds"/>).
    /// </summary>
    public string? VisibleCellId { get; private set; }

    /// <summary>Every cell on screen: the current desktop on each monitor.</summary>
    public IReadOnlySet<string> VisibleCellIds => _visible;

    private readonly HashSet<string> _visible = new();

    public bool IsVisible(Pet pet) => VisibleCellId is null || _visible.Contains(pet.CellId);

    /// <summary>The user is now looking at this cell (one monitor): see <see cref="ShowCells"/>.</summary>
    public void ShowCell(Cell cell, DateTime nowUtc) => ShowCells(new[] { cell }, nowUtc);

    /// <summary>
    /// The user is now looking at these cells: the current virtual desktop on every monitor, the main monitor first.
    /// After a switch of virtual desktop, pets left on the other desktops let go of anything screen-bound (held by
    /// the mouse, peeking at a window that is no longer on screen); follow-mode pets start coming over a moment later.
    /// </summary>
    public void ShowCells(IReadOnlyList<Cell> cells, DateTime nowUtc)
    {
        if (cells.Count == 0) return;
        var main = cells[0];
        if (VisibleCellId == main.Id && _visible.SetEquals(cells.Select(c => c.Id))) return;
        var first = VisibleCellId is null;
        var switched = !first && World.Find(VisibleCellId!) is { } was && was.DesktopId != main.DesktopId;
        VisibleCellId = main.Id;
        _visible.Clear();
        foreach (var c in cells) _visible.Add(c.Id);
        foreach (var pet in Pets)
        {
            if (_visible.Contains(pet.CellId)) continue;
            var cell = main;
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
        if (switched || (!first && World.MonitorOrder.Count < 2)) Travel.OnSwitched(Pets, _rng);
    }

    // ---------------------------------------------------------------- dress-up

    /// <summary>Pets that dress for the weather put on a raincoat in the rain and a scarf (and beanie) in the snow.</summary>
    private void TickWeatherWear()
    {
        var w = Weather;
        foreach (var pet in Pets)
        {
            if (!pet.Outfit.DressForWeather) { pet.WeatherWear = null; continue; }
            Dictionary<OutfitSlot, OutfitPiece>? wear = null;
            if (w.Raining) wear = new() { [OutfitSlot.Body] = OutfitCatalog.RainWear };
            else if (w.Snowing)
            {
                wear = new() { [OutfitSlot.Neck] = pet.Outfit.Get(OutfitSlot.Neck)?.ItemId == "scarf" ? pet.Outfit.Get(OutfitSlot.Neck)! : OutfitCatalog.SnowScarf };
                if (pet.Outfit.Get(OutfitSlot.Head) is null) wear[OutfitSlot.Head] = OutfitCatalog.SnowHat;
            }
            pet.WeatherWear = wear;
        }
    }

    /// <summary>A piece left on the floor goes back on its owner after this long (somebody put it back on).</summary>
    public const float DroppedBackAfter = 600f;

    private float _sinceCouple;

    /// <summary>
    /// Pieces on the floor age and eventually go back on their owners; pets in matching outfits grow closer a little
    /// faster while they share a screen.
    /// </summary>
    private void TickDressUp(float dt)
    {
        foreach (var cell in World.Cells)
            foreach (var w in cell.Dropped.ToList())
            {
                w.Age += dt;
                if (w.Age >= DroppedBackAfter && w.CarriedBy is null) Return(cell, w);
            }

        _sinceCouple += dt;
        if (_sinceCouple < 20f) return;
        _sinceCouple = 0f;
        CoupleBonus();
    }

    /// <summary>Pets in matching outfits on the same screen grow a little closer (every 20 s).</summary>
    internal void CoupleBonus()
    {
        for (var i = 0; i < Pets.Count; i++)
        for (var j = i + 1; j < Pets.Count; j++)
        {
            var (a, b) = (Pets[i], Pets[j]);
            if (a.CellId != b.CellId) continue;
            var match = OutfitCatalog.Matching(a, b);
            if (match > 0) Social.AddFriendship(a, b, 0.03f * match);
        }
    }

    private void Return(Cell cell, DroppedWear w)
    {
        cell.Dropped.Remove(w);
        if (Pets.FirstOrDefault(p => p.Id == w.OwnerId) is { } owner) owner.Doffed.Remove(w.Slot);
    }

    /// <summary>Pet panel: puts back on everything this pet shook off (wherever it lies).</summary>
    public int PutBack(Pet pet)
    {
        var n = 0;
        foreach (var cell in World.Cells)
            foreach (var w in cell.Dropped.Where(w => w.OwnerId == pet.Id && w.CarriedBy is null).ToList())
            {
                Return(cell, w);
                n++;
            }
        // Something a dog is wearing right now comes back too.
        foreach (var other in Pets)
            if (other.Borrowed is { } borrowed)
                foreach (var slot in borrowed.Where(kv => pet.Doffed.Contains(kv.Key) && pet.Outfit.Get(kv.Key) == kv.Value).Select(kv => kv.Key).ToList())
                {
                    borrowed.Remove(slot);
                    foreach (var cell in World.Cells) cell.Dropped.RemoveAll(w => w.OwnerId == pet.Id && w.Slot == slot);
                    n++;
                }
        pet.Doffed.Clear();
        if (n > 0 && OutfitCatalog.FashionLove(pet) <= 0.35f) pet.Say(L.S("Hmph… fine", "흥… 알았어"), 1.8f);
        return n;
    }

    /// <summary>
    /// The user dressed the pet up: it reacts in character. Fashion lovers (Maltese, Persian…) are delighted, proud
    /// and wild breeds (Shiba, Husky…) shake themselves and grumble, the rest are fine with it.
    /// </summary>
    public void ReactToOutfit(Pet pet, bool undressed)
    {
        var love = OutfitCatalog.FashionLove(pet);
        if (undressed)
        {
            pet.Say(love < 0.35f ? L.S("Finally! Free!", "드디어 자유다!") : L.S("Oh… it's gone", "어… 없어졌네"), 2.2f);
            return;
        }
        if (love >= 0.7f)
        {
            pet.SetAnimation(pet.Species == PetSpecies.Dog ? "playbow" : "sit");
            pet.Say(L.S("Am I pretty? I'm pretty!", "나 예뻐? 예쁘지!"), 2.4f);
            pet.Needs.Fun = System.Math.Min(100f, pet.Needs.Fun + 10f);
        }
        else if (love <= 0.3f)
        {
            pet.SetAnimation("shake");
            pet.Say(L.S("Hmph… do I have to wear this?", "흥… 이거 꼭 입어야 해?"), 2.4f);
        }
        else
        {
            pet.Say(L.S("Hmm, not bad", "음, 나쁘지 않네"), 2f);
        }
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
                if (IsVisible(pet) && !pet.Hidden && pet.Bubble is null && pet.Layer == PetLayer.Backyard && !pet.Hovered &&
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
        _now = nowUtc;

        User.Cursor = Debug.CursorOverride?.Invoke() ?? _platform.GetCursorPosition();
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
        Talk.Tick(this, _rng, dt);
        _sinceAgentLine += dt;
        _noteWeather = _sinceWeatherNote >= 60f;
        _sinceWeatherNote = _noteWeather ? 0f : _sinceWeatherNote + dt;

        foreach (var c in World.Cells) c.Ball?.Step(dt, c.Map);
        Travel.Tick(this, dt, _rng);
        TickWeather(dt);
        TickChatter(dt);
        TickWeatherWear();
        TickDressUp(dt);

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

            // Under the mouse: a pet out for a stroll stops (and looks at you), so it can be picked up.
            if (pet.Hovered && pet.Engine.Current is WanderBehavior) pet.Engine.ForceStop(ctx);
            pet.Engine.Update(ctx, dt);
            Unstick(pet, ctx, dt);
            NoteDay(pet, nowUtc);
            pet.AnimationTime += dt;
            pet.TickBubble(dt);
            TurnToward(pet, dt);
            pet.Motion.EndFrame(pet, dt, cell.Map.DepthScale(pet.Position.Y));
        }
    }

    /// <summary>How long a pet may walk on the spot before it is helped out.</summary>
    public const float StuckAfter = 5f;

    private readonly Dictionary<Guid, (Vec2 Anchor, float Time)> _stuck = new();

    /// <summary>
    /// Watchdog: a pet that has been walking or running for <see cref="StuckAfter"/> seconds without getting anywhere
    /// is caught on something (the edge of the floor, a spot it can't reach). Put it back on the floor and let it
    /// pick something else to do, so it never treads on the spot for good.
    /// </summary>
    private void Unstick(Pet pet, BehaviorContext ctx, float dt)
    {
        var walking = pet.Animation is "walk" or "run" && pet.Hold == HoldState.None && !pet.Hidden
                      && pet.Layer == PetLayer.Backyard && pet.Session is null && pet.Doorway is null;
        if (!walking)
        {
            _stuck.Remove(pet.Id);
            return;
        }
        var map = ctx.Cell.Map;
        if (!_stuck.TryGetValue(pet.Id, out var s) || Vec2.Distance(pet.Position, s.Anchor) > 12f * map.DepthScale(pet.Position.Y))
        {
            _stuck[pet.Id] = (pet.Position, 0f);
            return;
        }
        s.Time += dt;
        _stuck[pet.Id] = s;
        if (s.Time < StuckAfter) return;
        _stuck.Remove(pet.Id);
        var was = pet.Engine.Current?.Name;
        pet.Engine.ForceStop(ctx);
        pet.Position = map.ClampToRoam(pet.Position);
        pet.SetAnimation("idle");
        Debug.Log?.Invoke($"Unstuck: {pet.Name} was walking on the spot ({was}) at {pet.Position}");
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
        Journal.Forget(pet);
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
        Journal.Owner(pet, "fed", nowUtc);
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
        Journal.Owner(pet, "carried", _now);
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

    public void BeginStroke(Pet pet)
    {
        pet.Hold = HoldState.Petted;
        Journal.Owner(pet, "petted", _now);
    }

    /// <summary>For the diary: what the pet is doing now (when it changes), who it plays with, the weather.</summary>
    private void NoteDay(Pet pet, DateTime nowUtc)
    {
        var doing = (pet.Engine.Current?.Name ?? "idle").Split(':')[0];
        if (!_lastDoing.TryGetValue(pet.Id, out var last) || last != doing)
        {
            _lastDoing[pet.Id] = doing;
            Journal.Did(pet, doing, nowUtc);
        }
        var session = pet.Session;
        if (!ReferenceEquals(_lastSession.GetValueOrDefault(pet.Id), session))
        {
            _lastSession[pet.Id] = session;
            if (session is not null)
                Journal.With(pet, ReferenceEquals(session.Initiator, pet) ? session.Partner : session.Initiator, session.Name.Split(':')[0], nowUtc);
        }
        if (_noteWeather && IsVisible(pet)) Journal.SawWeather(pet, Weather.Kind.ToString().ToLowerInvariant(), nowUtc);
    }

    // --- the user's coding agent (Claude Code hooks through the app's local server) ---

    private float _sinceAgentLine = 999f;

    /// <summary>
    /// The user's coding agent says what it is up to; the closest, awake pet on screen reacts:
    /// working (a cheer now and then), done (delighted), waiting (calls the owner), failed (comforts).
    /// Returns who reacted, or null.
    /// </summary>
    public Pet? AgentEvent(string kind)
    {
        var buddy = Pets.Where(p => IsVisible(p) && !p.Hidden && p.Hold == HoldState.None && p.Engine.Current is not SleepBehavior)
            .OrderByDescending(p => p.Bond.Value).FirstOrDefault();
        if (buddy is null) return null;
        var cat = buddy.Species == PetSpecies.Cat;
        string[] lines;
        switch (kind)
        {
            case "working" or "prompt" or "start":
                if (_sinceAgentLine < 90f) return null;           // not every tool call
                lines = cat
                    ? new[] { L.S("…I'm supervising.", "…감독 중이다냥."), L.S("Mrrp. Busy busy.", "냥. 바쁘다 바빠."), L.S("Is that a bug? Can I eat it?", "버그? 먹어도 돼냥?") }
                    : new[] { L.S("Go go go! Woof!", "힘내라 힘! 왈!"), L.S("Sniff sniff… smells like code!", "킁킁… 코드 냄새!"), L.S("We're working hard!", "우리 열일 중이야!") };
                break;
            case "done" or "stop" or "finished":
                lines = cat
                    ? new[] { L.S("Done. Now pet me.", "끝났다냥. 이제 쓰다듬어."), L.S("Hmph, not bad.", "흥, 나쁘지 않네.") }
                    : new[] { L.S("All done! Woof woof!", "다 됐대! 왈왈!"), L.S("We did it!", "해냈다!") };
                break;
            case "waiting" or "notification" or "input":
                lines = cat
                    ? new[] { L.S("Human. You're needed.", "집사. 너 부른다냥.") }
                    : new[] { L.S("Hey! It's calling you!", "집사! 불러! 불러!") };
                break;
            case "failed" or "error":
                lines = cat
                    ? new[] { L.S("…It happens.", "…그럴 수도 있지.") }
                    : new[] { L.S("It's okay! Try again!", "괜찮아! 다시 해 보자!") };
                break;
            default:
                return null;
        }
        _sinceAgentLine = 0f;
        buddy.Say(lines[_rng.Next(lines.Length)], kind is "waiting" or "notification" or "input" ? 4.5f : 3f);
        buddy.LookAt = User.Cursor;
        Journal.Owner(buddy, "cheered the owner's work", _now);
        return buddy;
    }

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
        Journal.Owner(pet, interaction.Id, nowUtc);
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

    private readonly HashSet<IntPtr> _seenWindows = new();
    private bool _seenAny;

    private void PollUser()
    {
        User.Foreground = _platform.GetForegroundWindow();
        NoticeNewWindow(User.Foreground);
        User.Idle = _platform.GetIdleTime();
        User.DoNotDisturb = _platform.IsDoNotDisturb();
        var onDesktop = Debug.CursorOverride?.Invoke() is not null || _platform.IsDesktopAt(User.Cursor);
        if (onDesktop != User.CursorOnDesktop && Debug.TraceHover) Debug.Log?.Invoke($"Hover: cursor on desktop {onDesktop} at {User.Cursor}");
        User.CursorOnDesktop = onDesktop;
    }

    /// <summary>
    /// A window the pets have not seen before came to the front: watchful dogs nearby may bark at it
    /// (the shy and wary ones more often). Windows that were already open when the app started do not count.
    /// </summary>
    private void NoticeNewWindow(Platform.ForegroundWindowInfo? fg)
    {
        if (fg is null || fg.Handle == IntPtr.Zero) return;
        var isNew = _seenWindows.Add(fg.Handle);
        if (_seenWindows.Count > 512) { _seenWindows.Clear(); _seenWindows.Add(fg.Handle); }
        if (!_seenAny) { _seenAny = true; return; }
        if (!isNew || fg.IsFullscreen || User.DoNotDisturb) return;
        var where = new Vec2(fg.Bounds.X + fg.Bounds.Width / 2f, fg.Bounds.Y + fg.Bounds.Height / 2f);
        foreach (var pet in Pets)
        {
            if (pet.Species != PetSpecies.Dog || pet.Hidden || !IsVisible(pet)) continue;
            var cell = World.Find(pet.CellId);
            if (cell is null || !cell.Bounds.Contains(where)) continue;
            if (_rng.NextDouble() < 0.25 + pet.Personality.Shyness * 0.5) pet.Alert = where;
        }
    }
}
