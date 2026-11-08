namespace PetFam.Core.Pets;

/// <summary>Personality parameters 0..1 (plan §4.4). Rolled at creation, drifts slowly with play.</summary>
public sealed class Personality
{
    public float Playfulness { get; set; }
    public float Shyness { get; set; }
    public float Activity { get; set; }
    public float Homebody { get; set; }
    public float Tidiness { get; set; }

    /// <summary>Born with the breed's temperament, give or take (no two Shibas are quite the same).</summary>
    public static Personality Roll(Breed breed, Random rng)
    {
        float Jitter(float center) => System.Math.Clamp(center + ((float)rng.NextDouble() * 2f - 1f) * 0.15f, 0f, 1f);
        return new Personality
        {
            Playfulness = Jitter(breed.Playfulness),
            Shyness = Jitter(breed.Shyness),
            Activity = Jitter(breed.Activity),
            Homebody = Jitter(breed.Homebody),
            Tidiness = Jitter(breed.Tidiness),
        };
    }

    public static Personality Roll(PetSpecies species, Random rng)
    {
        float Jitter(float center, float spread) =>
            System.Math.Clamp(center + ((float)rng.NextDouble() * 2f - 1f) * spread, 0f, 1f);

        return species switch
        {
            PetSpecies.Cat => new Personality
            {
                Playfulness = Jitter(0.55f, 0.25f),
                Shyness = Jitter(0.55f, 0.25f),
                Activity = Jitter(0.40f, 0.20f),
                Homebody = Jitter(0.60f, 0.20f),
                Tidiness = Jitter(0.85f, 0.10f),
            },
            PetSpecies.Dog => new Personality
            {
                Playfulness = Jitter(0.70f, 0.20f),
                Shyness = Jitter(0.20f, 0.15f),
                Activity = Jitter(0.70f, 0.20f),
                Homebody = Jitter(0.30f, 0.20f),
                Tidiness = Jitter(0.20f, 0.15f),
            },
            _ => new Personality { Playfulness = .5f, Shyness = .5f, Activity = .5f, Homebody = .5f, Tidiness = .5f },
        };
    }
}
