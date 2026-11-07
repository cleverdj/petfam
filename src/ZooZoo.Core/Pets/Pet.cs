using ZooZoo.Core.Behavior;
using ZooZoo.Core.Geometry;
using ZooZoo.Core.Interactions;

namespace ZooZoo.Core.Pets;

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

    /// <summary>Set by the host while the user holds the pet with the mouse (plan §7.1 쓰다듬기 / 안아 옮기기).</summary>
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
    public float Lift { get; set; }

    /// <summary>Inside its house (sleeping): not drawn, not touchable.</summary>
    public bool Hidden { get; set; }

    /// <summary>The two-pet encounter this pet is part of, if any.</summary>
    public Social.SocialSession? Session { get; set; }

    public void Say(string text, float seconds = 2.2f)
    {
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

    public Pet(PetSpecies species, string name, DateTime nowUtc, Random rng)
    {
        Species = species;
        Name = name;
        Bond = new Bond(nowUtc);
        Personality = Personality.Roll(species, rng);
        Engine = BehaviorEngine.CreateDefault(species);
    }

    public void SetAnimation(string clip)
    {
        if (Animation == clip) return;
        Animation = clip;
        AnimationTime = 0f;
    }
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
