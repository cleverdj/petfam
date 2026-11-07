using ZooZoo.Core.Behavior;
using ZooZoo.Core.Behavior.Behaviors;
using ZooZoo.Core.Geometry;
using ZooZoo.Core.Interactions;
using ZooZoo.Core.Pets;
using ZooZoo.Core.Platform;
using ZooZoo.Core.Social;
using ZooZoo.Core.World;

namespace ZooZoo.Core;

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

    private Vec2 _lastCursor;
    private bool _hasCursor;

    public Simulation(IPlatform platform, Random? rng = null)
    {
        _platform = platform;
        _rng = rng ?? new Random();
    }

    public Pet AddPet(PetSpecies species, string name, Cell cell, DateTime nowUtc)
    {
        var pet = new Pet(species, name, nowUtc, _rng)
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

        var local = nowUtc.ToLocalTime();
        var isDaytime = local.Hour is >= 7 and < 19;

        Director.Tick(Pets, p => World.Find(p.CellId) ?? World.AnyCell(), Social, _rng, nowUtc, dt);

        foreach (var pet in Pets)
        {
            var cell = World.Find(pet.CellId) ?? World.AnyCell();
            if (cell is null) continue;

            var sleeping = pet.Engine.Current is SleepBehavior;
            pet.Needs.Tick(dt, User.IsActive, sleeping);
            pet.Bond.Tick(nowUtc, User.IsActive);

            var inSun = cell.Map.ZoneAt(pet.Position, ZoneType.Sun) is not null;
            pet.Appearance.Tick(dt, isDaytime, inSun);

            UpdateLook(pet, sleeping);

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
            };

            foreach (var b in pet.Engine.Behaviors)
            {
                if (b is PeekBehavior peek) peek.TickCooldown(dt);
                if (b is SwimBehavior swim) swim.TickCooldown(dt);
                if (b is HuntCursorBehavior hunt) hunt.TickCooldown(dt);
            }

            pet.Engine.Update(ctx, dt);
            pet.AnimationTime += dt;
            pet.TickBubble(dt);
            TurnToward(pet, dt);
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

    private static readonly string[] CatNames = { "나비", "치즈", "루루", "보리", "까미" };
    private static readonly string[] DogNames = { "초코", "두부", "몽이", "해피", "별이" };
    public const int MaxPets = 6;

    /// <summary>Adds another pet with a fresh name at a random spot on the floor. Null when full.</summary>
    public Pet? AdoptPet(PetSpecies species, Cell cell, DateTime nowUtc)
    {
        if (Pets.Count >= MaxPets) return null;
        var used = Pets.Select(p => p.Name).ToHashSet();
        var pool = species == PetSpecies.Cat ? CatNames : DogNames;
        var name = pool.FirstOrDefault(n => !used.Contains(n)) ?? $"{species.DisplayName()} {Pets.Count + 1}";
        var pet = AddPet(species, name, cell, nowUtc);
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
        var step = TurnSpeedDegPerSec * dt;
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
        return pet.Bond.Pet(nowUtc);
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
        if (sleeping || pet.Hold == HoldState.Carried || !User.CursorOnDesktop)
        {
            pet.LookAt = null;
            return;
        }

        var near = Vec2.Distance(User.Cursor, pet.Position) <= LookDistance;
        pet.LookAt = near ? User.Cursor : null;

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
