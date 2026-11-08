namespace PetFam.Core.Pets;

/// <summary>Life stages (plan §4.6).</summary>
public enum LifeStage
{
    Baby,
    Junior,
    Adult,
    Senior,
}

/// <summary>
/// How a life stage changes needs and movement, relative to an adult (1.0). Based on real cats and dogs:
/// babies sleep most of the day and play in short bursts, juniors play the most, seniors sleep more and run less.
/// </summary>
public sealed record StageTraits(
    float EnergyDecay, float EnergyRegen, float FunDecay, float HungerDecay,
    float Speed, float Run, float Leap, float Sleepiness, float SleepThreshold)
{
    public static StageTraits Of(LifeStage stage, PetSpecies species)
    {
        var t = stage switch
        {
            LifeStage.Baby => new StageTraits(1.6f, 1.5f, 1.3f, 1.4f, 0.8f, 1.1f, 0.6f, 0.12f, 70f),
            LifeStage.Junior => new StageTraits(1.0f, 1.1f, 1.5f, 1.2f, 1.15f, 1.1f, 1.2f, 0f, 60f),
            LifeStage.Senior => new StageTraits(1.3f, 0.7f, 0.6f, 0.9f, 0.75f, 0.75f, 0.5f, 0.15f, 70f),
            _ => new StageTraits(1f, 1f, 1f, 1f, 1f, 1f, 1f, 0f, 60f),
        };
        // Cats sleep about a fifth more than dogs at every age.
        return species == PetSpecies.Cat ? t with { EnergyDecay = t.EnergyDecay * 1.2f, Sleepiness = t.Sleepiness + 0.05f } : t;
    }
}

/// <summary>
/// A pet's age, counted in days spent together: a day counts once the user has been active with the app running
/// for ten minutes that day, so a pet does not grow up while the computer is off. Adopted pets arrive as two-month-old
/// babies; about two weeks later they are juniors, after a month and a half adults, after a year seniors. Nobody dies.
/// </summary>
public sealed class Growth
{
    public const int JuniorAt = 15;
    public const int AdultAt = 46;
    public const int SeniorAt = 366;
    public const float MinutesForADay = 10f;

    /// <summary>Days spent together (the pet's age clock).</summary>
    public int Days { get; set; }

    public DateTime AdoptedUtc { get; set; }

    /// <summary>Local date the active minutes below belong to.</summary>
    public DateOnly ActiveDate { get; set; }

    public float ActiveMinutes { get; set; }

    /// <summary>Today already counted as a day together.</summary>
    public bool CountedToday { get; set; }

    /// <summary>Stage the user was last told about (for the "grew up" celebration).</summary>
    public LifeStage AnnouncedStage { get; set; }

    /// <summary>Days of the last anniversary celebrated.</summary>
    public int LastAnniversary { get; set; }

    public static readonly int[] Anniversaries = { 30, 100, 365, 730, 1095 };

    public Growth(DateTime adoptedUtc, int days = 0)
    {
        AdoptedUtc = adoptedUtc;
        Days = days;
        AnnouncedStage = StageAt(days);
        LastAnniversary = Anniversaries.LastOrDefault(a => a <= days);
    }

    public LifeStage Stage => StageAt(Days);

    public static LifeStage StageAt(int days) => days switch
    {
        < JuniorAt => LifeStage.Baby,
        < AdultAt => LifeStage.Junior,
        < SeniorAt => LifeStage.Adult,
        _ => LifeStage.Senior,
    };

    /// <summary>0 at adoption … 1 fully grown (day AdultAt). Drives body proportions.</summary>
    public float BodyGrowth => System.Math.Clamp(Days / (float)AdultAt, 0f, 1f);

    /// <summary>0 before senior … 1 about four months into old age. Drives grey fur and a slower gait.</summary>
    public float Seniority => System.Math.Clamp((Days - SeniorAt) / 120f, 0f, 1f);

    /// <summary>0..1 through the current stage (the senior stage has no end: shown as full).</summary>
    public float StageProgress => Stage switch
    {
        LifeStage.Baby => Days / (float)JuniorAt,
        LifeStage.Junior => (Days - JuniorAt) / (float)(AdultAt - JuniorAt),
        LifeStage.Adult => (Days - AdultAt) / (float)(SeniorAt - AdultAt),
        _ => 1f,
    };

    public bool IsAnniversaryToday => Anniversaries.Contains(Days) && CountedToday;

    /// <summary>Age in months as the animal would count it.</summary>
    public float AgeMonths(PetSpecies species)
    {
        var junior = species == PetSpecies.Dog ? 18f : 12f;
        var adult = species == PetSpecies.Dog ? 18f : 12f;
        return Days switch
        {
            < JuniorAt => Lerp(2f, 6f, Days / (float)JuniorAt),
            < AdultAt => Lerp(6f, junior, (Days - JuniorAt) / (float)(AdultAt - JuniorAt)),
            < SeniorAt => Lerp(adult, 84f, (Days - AdultAt) / (float)(SeniorAt - AdultAt)),
            _ => 84f + (Days - SeniorAt) / 60f * 12f,
        };
    }

    public string AgeLabel(PetSpecies species)
    {
        var m = AgeMonths(species);
        if (L.Ko)
            return m < 12f ? $"생후 {(int)m}개월" : m < 24f && (int)m % 12 != 0 ? $"{(int)(m / 12)}살 {(int)m % 12}개월" : $"{(int)(m / 12)}살";
        var y = (int)(m / 12);
        return m < 12f ? $"{(int)m} months old"
            : m < 24f && (int)m % 12 != 0 ? $"{y} yr {(int)m % 12} mo"
            : $"{y} {(y == 1 ? "year" : "years")} old";
    }

    public static string StageName(LifeStage s) => s switch
    {
        LifeStage.Baby => L.S("Baby", "새끼"),
        LifeStage.Junior => L.S("Junior", "청소년"),
        LifeStage.Adult => L.S("Adult", "성체"),
        _ => L.S("Senior", "노령"),
    };

    public static string StageEmoji(LifeStage s) => s switch
    {
        LifeStage.Baby => "🍼",
        LifeStage.Junior => "🌱",
        LifeStage.Adult => "🐾",
        _ => "🍂",
    };

    public enum Event { None, NewDay, GrewUp, Anniversary }

    /// <summary>
    /// Advances the day clock. Active time counts toward today; the tenth active minute makes today a day together.
    /// Returns what happened (at most one event per call).
    /// </summary>
    public Event Tick(float dtSeconds, bool userActive, DateOnly today)
    {
        if (today != ActiveDate)
        {
            ActiveDate = today;
            ActiveMinutes = 0f;
            CountedToday = false;
        }
        if (userActive) ActiveMinutes += dtSeconds / 60f;
        if (CountedToday || ActiveMinutes < MinutesForADay) return Event.None;

        CountedToday = true;
        Days++;
        return Celebrate();
    }

    /// <summary>Skips ahead (test option / debug). Returns the last event it produced.</summary>
    public Event AddDays(int days)
    {
        Days = System.Math.Max(0, Days + days);
        return Celebrate();
    }

    private Event Celebrate()
    {
        if (Stage != AnnouncedStage)
        {
            AnnouncedStage = Stage;
            return Event.GrewUp;
        }
        var anniversary = Anniversaries.LastOrDefault(a => a <= Days);
        if (anniversary > LastAnniversary)
        {
            LastAnniversary = anniversary;
            return Event.Anniversary;
        }
        return Event.NewDay;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * System.Math.Clamp(t, 0f, 1f);
}
