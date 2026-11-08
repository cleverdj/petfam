namespace PetFam.Core.Pets;

public enum MoodLevel
{
    Bad,
    Okay,
    Good,
}

/// <summary>Overall mood from the needs (the Sims "motives"), shown by the plumbob colour and the panel.</summary>
public static class MoodCalc
{
    /// <summary>Cleanliness 0..100 derived from the appearance state (wet counts half, mud fully).</summary>
    public static float Hygiene(Pet pet) =>
        100f * (1f - System.Math.Clamp(System.Math.Max(pet.Appearance.Wet * 0.5f, pet.Appearance.Mud), 0f, 1f));

    /// <summary>0..100. The lowest need drags the mood down more than the average lifts it.</summary>
    public static float Mood(Pet pet)
    {
        var n = pet.Needs;
        float[] values = { n.Hunger, n.Energy, n.Fun, n.Affection, Hygiene(pet) };
        var avg = values.Average();
        var min = values.Min();
        return System.Math.Clamp(avg * 0.6f + min * 0.4f, 0f, 100f);
    }

    public static MoodLevel Level(Pet pet)
    {
        var m = Mood(pet);
        return m >= 60f ? MoodLevel.Good : m >= 30f ? MoodLevel.Okay : MoodLevel.Bad;
    }

    public static string Label(MoodLevel level) => level switch
    {
        MoodLevel.Good => L.S("Happy", "기분 좋음"),
        MoodLevel.Okay => L.S("Okay", "그럭저럭"),
        _ => L.S("Unhappy", "기분 나쁨"),
    };
}
