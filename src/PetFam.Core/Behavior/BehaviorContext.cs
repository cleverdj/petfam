using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Behavior;

/// <summary>What the user is doing right now, sampled from IPlatform (plan §5.1 user_context).</summary>
public sealed class UserContext
{
    public ForegroundWindowInfo? Foreground { get; set; }
    public Vec2 Cursor { get; set; }

    /// <summary>True when the cursor is over the bare desktop (or one of our windows), i.e. pets there are visible.</summary>
    public bool CursorOnDesktop { get; set; }

    /// <summary>Smoothed pointer speed in pixels per second (prey movement for the cat's hunting instinct).</summary>
    public float CursorSpeed { get; set; }

    /// <summary>Seconds since the pointer last moved.</summary>
    public float CursorStill { get; set; }

    public TimeSpan Idle { get; set; }
    public bool DoNotDisturb { get; set; }

    public bool IsActive => Idle < TimeSpan.FromSeconds(30);
}

public sealed class DebugOptions
{
    /// <summary>Spike: let pets peek regardless of bond stage so the mechanic can be tested from minute one.</summary>
    public bool AllowPeekAlways { get; set; } = true;

    public bool ShowHud { get; set; }

    /// <summary>Paint the recognised floor over the screen (test option).</summary>
    public bool ShowHabitat { get; set; }
}

public sealed class BehaviorContext
{
    public required Pet Pet { get; init; }
    public required Cell Cell { get; init; }
    public required UserContext User { get; init; }
    public required Random Rng { get; init; }
    public required DebugOptions Debug { get; init; }
    public DateTime NowUtc { get; set; }
    public bool IsDaytime { get; set; } = true;

    /// <summary>The real weather where the user is.</summary>
    public World.WeatherNow Weather { get; init; } = World.WeatherNow.Clear;

    /// <summary>The pet is on the desktop the user is looking at (false: on another virtual desktop).</summary>
    public bool Visible { get; init; } = true;

    /// <summary>All pets (for pecking order and sharing the bowl).</summary>
    public IReadOnlyList<Pet> Pets { get; init; } = Array.Empty<Pet>();

    /// <summary>Every pet but this one.</summary>
    public IEnumerable<Pet> Others => Pets.Where(p => !ReferenceEquals(p, Pet));

    public Social.SocialState? Social { get; init; }

    public float RandomRange(float min, float max) => min + (float)Rng.NextDouble() * (max - min);
}
