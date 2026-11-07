namespace ZooZoo.Core.Pets;

/// <summary>
/// Pet needs, 0..100 (plan §4.2). Decay rates are per real-time hour.
/// </summary>
public sealed class Needs
{
    public float Hunger { get; set; } = 100f;
    public float Energy { get; set; } = 100f;
    public float Fun { get; set; } = 100f;
    public float Affection { get; set; } = 100f;
    public float Curiosity { get; set; } = 0f;

    // Rates per hour.
    public const float HungerDecayPerHour = 100f / 12f;      // two meals a day
    public const float EnergyDecayPerHour = 5f;
    public const float EnergyRegenPerHour = 35f;              // while sleeping
    public const float FunDecayPerHour = 100f / 24f;
    public const float AffectionDecayPerHour = 100f / 24f;
    public const float CuriosityGainPerHour = 900f;           // ~15 per minute while the user is active

    public void Tick(float dtSeconds, bool userActive, bool sleeping)
    {
        var h = dtSeconds / 3600f;
        Hunger = Clamp(Hunger - HungerDecayPerHour * h);
        Energy = Clamp(sleeping ? Energy + EnergyRegenPerHour * h : Energy - EnergyDecayPerHour * h);
        Fun = Clamp(Fun - FunDecayPerHour * h);
        Affection = Clamp(Affection - AffectionDecayPerHour * h);
        if (userActive)
            Curiosity = Clamp(Curiosity + CuriosityGainPerHour * h);
    }

    public void Feed() => Hunger = 100f;

    public void Play(float amount = 40f) => Fun = Clamp(Fun + amount);

    public void ReceiveAffection(float amount = 25f) => Affection = Clamp(Affection + amount);

    public void SpendCuriosity(float amount) => Curiosity = Clamp(Curiosity - amount);

    private static float Clamp(float v) => System.Math.Clamp(v, 0f, 100f);
}
