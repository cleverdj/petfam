using PetFam.Core.World;

namespace PetFam.Core.Pets;

/// <summary>How a pet moves between virtual desktops (plan §6.4.3). Set per pet.</summary>
public enum TravelMode
{
    /// <summary>Goes where needs and curiosity take it; visits the desktop the user stays on for a while.</summary>
    Free,

    /// <summary>Runs in from the screen edge a moment after the user switches desktops.</summary>
    Follow,

    /// <summary>Lives on its home desktop only (comes when called, then goes back).</summary>
    Home,
}

public enum TripReason
{
    /// <summary>Following the user to the desktop they switched to.</summary>
    Follow,

    /// <summary>The user called the pet from the tray.</summary>
    Call,

    /// <summary>Curiosity: visiting the user, or going for a look at a neighbouring desktop.</summary>
    Visit,

    /// <summary>Hungry or sleepy: going to the desktop with food or a house.</summary>
    Need,

    /// <summary>A home-mode pet going back home.</summary>
    Home,
}

/// <summary>A trip to another virtual desktop. The pet walks off one screen edge and comes in at the other side.</summary>
public sealed class Trip
{
    public required Cell To { get; init; }
    public required TripReason Reason { get; init; }

    /// <summary>+1: leaves through the right edge and enters from the left (the next desktop). -1: the other way.</summary>
    public int Direction { get; init; } = 1;

    public bool Run { get; init; }

    /// <summary>Seconds "between rooms": the pet is on neither screen.</summary>
    public float Gap { get; init; } = 1.5f;
}
