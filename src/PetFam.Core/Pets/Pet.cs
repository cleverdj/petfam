using PetFam.Core.Behavior;
using PetFam.Core.Geometry;
using PetFam.Core.Interactions;

namespace PetFam.Core.Pets;

public sealed class Pet
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public PetSpecies Species { get; init; }

    public Needs Needs { get; } = new();
    public Bond Bond { get; }
    public Personality Personality { get; init; }
    public Appearance Appearance { get; } = new();

    // --- runtime state (not persisted except position/cell) ---

    /// <summary>Id of the cell (desktop × monitor) the pet is in. See World.</summary>
    public string CellId { get; set; } = "";

    /// <summary>Feet position in screen pixels.</summary>
    public Vec2 Position { get; set; }

    /// <summary>+1 facing right, -1 facing left (derived from Heading; kept for 2D helpers).</summary>
    public float Facing { get; set; } = 1f;

    /// <summary>
    /// Direction the body faces, in screen degrees: 0 = right, 90 = toward the viewer (down the screen),
    /// 180 = left, 270 = away (up the screen). Turns smoothly toward TargetHeading.
    /// </summary>
    public float Heading { get; set; }

    public float TargetHeading { get; set; }

    /// <summary>Acceleration, braking and measured motion (see Locomotion).</summary>
    public Locomotion Motion { get; } = new();

    /// <summary>Set the desired heading from a screen-space movement vector.</summary>
    public void FaceToward(Vec2 delta)
    {
        if (delta.Length < 0.5f) return;
        TargetHeading = MathF.Atan2(delta.Y, delta.X) * 180f / MathF.PI;
    }

    public PetLayer Layer { get; set; } = PetLayer.Backyard;

    /// <summary>Animation clip name the renderer should play (idle, walk, sit, sleep, peek, shake...).</summary>
    public string Animation { get; set; } = "idle";

    /// <summary>Seconds since the current animation started.</summary>
    public float AnimationTime { get; set; }

    public PeekState? Peek { get; set; }

    /// <summary>Set by the host while the user holds the pet with the mouse (plan §7.1 petting / carrying).</summary>
    public HoldState Hold { get; set; }

    /// <summary>Seconds left of enjoying a single pat (a click). The pet stops and stays in the petted pose meanwhile.</summary>
    public float PatTimer { get; set; }

    /// <summary>Sims-style action queue: what the user asked for, in order. The head runs first.</summary>
    public List<QueuedAction> Queue { get; } = new();

    public const int MaxQueue = 5;

    /// <summary>Selected in the UI: shows the plumbob and the needs panel.</summary>
    public bool Selected { get; set; }

    /// <summary>Speech bubble text above the head, and how long it stays.</summary>
    public string? Bubble { get; private set; }
    public float BubbleTimer { get; private set; }

    public DateTime LastTreatUtc { get; set; } = DateTime.MinValue;

    /// <summary>Height above the floor in pixels (depth 1): jumps, pounces, startle hops.</summary>
    /// <summary>Height of a hop in pixels. Babies and seniors jump lower (stage leap factor applied here).</summary>
    public float Lift
    {
        get => _lift;
        set => _lift = value * Traits.Leap;
    }
    private float _lift;

    /// <summary>How this pet moves between virtual desktops (plan §6.4.3).</summary>
    public TravelMode Travel { get; set; }

    /// <summary>The cell a home-mode pet lives in.</summary>
    public string? HomeCellId { get; set; }

    /// <summary>A trip to another desktop in progress (or about to start), if any.</summary>
    public Trip? Trip { get; set; }

    /// <summary>Age, life stage and the celebration bookkeeping (plan §4.6).</summary>
    public Growth Growth { get; set; }

    public LifeStage Stage => Growth.Stage;

    /// <summary>Life stage factors, then the breed's own (a Corgi eats more, a Persian sleeps more, a Munchkin jumps lower).</summary>
    public StageTraits Traits
    {
        get
        {
            var t = StageTraits.Of(Growth.Stage, Species);
            var b = Breed;
            return t with
            {
                EnergyDecay = t.EnergyDecay * b.Energy,
                HungerDecay = t.HungerDecay * b.Appetite,
                Speed = t.Speed * b.Speed,
                Leap = t.Leap * b.Leap,
                Sleepiness = t.Sleepiness + b.Sleepiness,
            };
        }
    }

    /// <summary>Walking speed for this pet's species and age, pixels per second at depth 1.</summary>
    public float WalkSpeed() => Species.WalkSpeed() * Traits.Speed;

    /// <summary>Run speed factor for this pet's species and age.</summary>
    public float RunMultiplier() => Species.RunMultiplier() * Traits.Run;

    /// <summary>Set when the pet grew into a new stage or reached an anniversary; the renderer throws confetti once.</summary>
    public Growth.Event Celebration { get; set; }

    /// <summary>Inside its house (sleeping): not drawn, not touchable.</summary>
    public bool Hidden { get; set; }

    /// <summary>Walking in or out through the door of a house the user built (the renderer clips the pet to the doorway).</summary>
    public Doorway? Doorway { get; set; }

    /// <summary>The house the pet is inside (while Hidden), for the "who is home" sign.</summary>
    public Guid? InsideHouse { get; set; }

    /// <summary>The two-pet encounter this pet is part of, if any.</summary>
    public Social.SocialSession? Session { get; set; }

    public void Say(string text, float seconds = 2.2f)
    {
        // Babies can't say it properly yet.
        if (Growth.Stage == LifeStage.Baby)
            text = text.Replace("냥", "먕").Replace("왈", "앙").Replace("멍", "앙")
                .Replace("Meow", "Mew").Replace("meow", "mew").Replace("Woof", "Yip").Replace("woof", "yip");
        Bubble = text;
        BubbleTimer = seconds;
    }

    public void TickBubble(float dt)
    {
        if (BubbleTimer <= 0f) return;
        BubbleTimer -= dt;
        if (BubbleTimer <= 0f) Bubble = null;
    }

    /// <summary>Screen point the pet is looking at (cursor nearby), or null.</summary>
    public Vec2? LookAt { get; set; }

    /// <summary>Raised when a sleep ends so the stretch behavior runs next.</summary>
    public bool JustWokeUp { get; set; }

    public BehaviorEngine Engine { get; }

    /// <summary>Breed: looks, temperament, habits and way of talking.</summary>
    public Breed Breed { get; }

    /// <summary>Says a line in this breed's voice (if it has one for the situation).</summary>
    public void SayAs(Func<BreedVoice, Loc[]> pick, Random rng, float seconds = 2.4f)
    {
        var line = Breed.Line(pick, rng);
        if (line.Length > 0) Say(line, seconds);
    }

    public Pet(PetSpecies species, string name, DateTime nowUtc, Random rng, Breed? breed = null)
    {
        Species = species;
        Breed = breed is { } b && b.Species == species ? b : Breed.Default(species);
        Name = name;
        Bond = new Bond(nowUtc) { Rate = Breed.BondRate };
        Growth = new Growth(nowUtc);   // newly adopted pets are two-month-old babies
        Travel = Breed.DefaultTravel;
        Personality = Personality.Roll(Breed, rng);
        Engine = BehaviorEngine.CreateDefault(species);
    }

    public void SetAnimation(string clip)
    {
        if (Animation == clip) return;
        Animation = clip;
        AnimationTime = 0f;
    }
}

/// <summary>A pet passing through a house door: where the door is, so the part already inside is hidden.</summary>
public sealed class Doorway
{
    /// <summary>Middle of the door's threshold (the house base line), screen pixels.</summary>
    public required Vec2 Door { get; init; }

    /// <summary>Depth scale of the house (door size).</summary>
    public required float HouseDepth { get; init; }

    public required Guid HouseId { get; init; }
}

public enum HoldState
{
    None,
    Carried,
    Petted,

    /// <summary>The pie menu is open for this pet: it stops and looks at you.</summary>
    Attention,
}

public enum PeekEdge
{
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>State of a window-edge peek. Anchor is the point on the window edge the head emerges from.</summary>
public sealed class PeekState
{
    public PeekEdge Edge { get; init; }
    public Vec2 Anchor { get; init; }
    public RectI WindowRect { get; init; }
    public IntPtr WindowHandle { get; init; }

    /// <summary>0 = fully hidden behind the window, 1 = fully emerged.</summary>
    public float Progress { get; set; }
}
