namespace ZooZoo.Core.Pets;

/// <summary>Personality parameters 0..1 (plan §4.4). Rolled at creation, drifts slowly with play.</summary>
public sealed class Personality
{
    public float Playfulness { get; set; }
    public float Shyness { get; set; }
    public float Activity { get; set; }
    public float Homebody { get; set; }
    public float Tidiness { get; set; }

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
