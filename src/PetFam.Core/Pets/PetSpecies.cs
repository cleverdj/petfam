namespace PetFam.Core.Pets;

public enum PetSpecies
{
    Cat,
    Dog,
}

/// <summary>Which of the three presence layers the pet is currently living in (plan §3).</summary>
public enum PetLayer
{
    /// <summary>Desktop layer: above the wallpaper, behind every window.</summary>
    Backyard,

    /// <summary>Peeking from the edge of the foreground window.</summary>
    WindowEdge,

    /// <summary>Topmost overlay, click-through. Short visits only.</summary>
    Front,
}

public static class PetSpeciesExtensions
{
    /// <summary>Walking speed in pixels per second at 100% DPI.</summary>
    public static float WalkSpeed(this PetSpecies s) => s switch
    {
        PetSpecies.Cat => 90f,
        PetSpecies.Dog => 130f,
        _ => 100f,
    };

    /// <summary>Run speed multiplier over walk speed.</summary>
    public static float RunMultiplier(this PetSpecies s) => s switch
    {
        PetSpecies.Cat => 2.4f,
        PetSpecies.Dog => 2.1f,
        _ => 2f,
    };

    /// <summary>Distance from the feet origin to the food bowl centre when eating (pixels at depth 1).</summary>
    public static float EatReach(this PetSpecies s) => s switch
    {
        PetSpecies.Cat => 50f,
        PetSpecies.Dog => 48f,
        _ => 60f,
    };

    public static string DisplayName(this PetSpecies s) => s switch
    {
        PetSpecies.Cat => L.S("Cat", "고양이"),
        PetSpecies.Dog => L.S("Dog", "강아지"),
        _ => s.ToString(),
    };
}
