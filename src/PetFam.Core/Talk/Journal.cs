using PetFam.Core.Pets;

namespace PetFam.Core.Talk;

/// <summary>What one pet got up to on one day: the raw material for its diary. Saved, so a restart keeps the day.</summary>
public sealed class DayLog
{
    public Guid PetId { get; set; }
    public DateOnly Date { get; set; }

    /// <summary>Things it did and how often ("sleep" 3, "swim" 1, "dig" 2…).</summary>
    public Dictionary<string, int> Did { get; set; } = new();

    /// <summary>Who it spent time with and how ("Peanut: playbow", "Momo: talk").</summary>
    public List<string> With { get; set; } = new();

    /// <summary>A few things it said or heard in conversations.</summary>
    public List<string> Talk { get; set; } = new();

    /// <summary>What the owner did with it (fed, petted, carried, dressed it up, chatted…).</summary>
    public Dictionary<string, int> Owner { get; set; } = new();

    /// <summary>The weather it saw.</summary>
    public List<string> Weather { get; set; } = new();

    public bool Empty => Did.Count == 0 && With.Count == 0 && Owner.Count == 0 && Talk.Count == 0;

    /// <summary>The day in plain words for a prompt.</summary>
    public string Describe(Func<Guid, string?> nameOf)
    {
        var parts = new List<string>();
        if (Did.Count > 0) parts.Add("Did: " + string.Join(", ", Did.OrderByDescending(kv => kv.Value).Select(kv => kv.Value > 1 ? $"{kv.Key} ×{kv.Value}" : kv.Key)));
        if (With.Count > 0) parts.Add("With others: " + string.Join(", ", With.Distinct()));
        if (Owner.Count > 0) parts.Add("The owner: " + string.Join(", ", Owner.Select(kv => kv.Value > 1 ? $"{kv.Key} ×{kv.Value}" : kv.Key)));
        if (Talk.Count > 0) parts.Add("Conversations: " + string.Join(" / ", Talk.TakeLast(8)));
        if (Weather.Count > 0) parts.Add("Weather: " + string.Join(", ", Weather.Distinct()));
        return string.Join("\n", parts);
    }
}

/// <summary>A diary entry a pet wrote (in its own voice) about one day.</summary>
public sealed class DiaryEntry
{
    public Guid PetId { get; set; }
    public DateOnly Date { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>A weekly story about all the pets, written from their diaries.</summary>
public sealed class StoryEntry
{
    public DateOnly Date { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>
/// Keeps each pet's day (what it did, who with, what the owner did) and the diaries and stories written from it.
/// Days stay until their diary is written, for a few days at most.
/// </summary>
public sealed class Journal
{
    public const int KeepDays = 4;
    public const int MaxDiaries = 400;

    /// <summary>Things too ordinary to write home about.</summary>
    private static readonly HashSet<string> Ordinary = new()
    {
        "idle", "wander", "held", "social", "queued", "approach-food", "wait-for-food", "travel", "look", "turn",
    };

    public List<DayLog> Days { get; } = new();
    public List<DiaryEntry> Diaries { get; } = new();
    public List<StoryEntry> Stories { get; } = new();

    public DayLog Day(Pet pet, DateOnly date)
    {
        var log = Days.FirstOrDefault(d => d.PetId == pet.Id && d.Date == date);
        if (log is null)
        {
            log = new DayLog { PetId = pet.Id, Date = date };
            Days.Add(log);
            if (date.DayNumber > KeepDays) Days.RemoveAll(d => d.Date < date.AddDays(-KeepDays));
        }
        return log;
    }

    public static DateOnly Today(DateTime nowUtc) => DateOnly.FromDateTime(nowUtc.ToLocalTime());

    public void Did(Pet pet, string what, DateTime nowUtc)
    {
        if (Ordinary.Contains(what)) return;
        var d = Day(pet, Today(nowUtc)).Did;
        d[what] = d.GetValueOrDefault(what) + 1;
    }

    public void With(Pet pet, Pet other, string how, DateTime nowUtc)
    {
        var w = Day(pet, Today(nowUtc)).With;
        var line = $"{other.Name}: {how}";
        if (!w.Contains(line)) w.Add(line);
    }

    public void Talked(Pet pet, IEnumerable<string> lines, DateTime nowUtc)
    {
        var t = Day(pet, Today(nowUtc)).Talk;
        t.AddRange(lines);
        if (t.Count > 16) t.RemoveRange(0, t.Count - 16);
    }

    public void Owner(Pet pet, string what, DateTime nowUtc)
    {
        var o = Day(pet, Today(nowUtc)).Owner;
        o[what] = o.GetValueOrDefault(what) + 1;
    }

    public void SawWeather(Pet pet, string kind, DateTime nowUtc)
    {
        var w = Day(pet, Today(nowUtc)).Weather;
        if (!w.Contains(kind)) w.Add(kind);
    }

    public DiaryEntry? DiaryOf(Pet pet, DateOnly date) => Diaries.FirstOrDefault(d => d.PetId == pet.Id && d.Date == date);

    public IEnumerable<DiaryEntry> DiariesOf(Pet pet) => Diaries.Where(d => d.PetId == pet.Id).OrderByDescending(d => d.Date);

    public void Write(Pet pet, DateOnly date, string text)
    {
        Diaries.RemoveAll(d => d.PetId == pet.Id && d.Date == date);
        Diaries.Add(new DiaryEntry { PetId = pet.Id, Date = date, Text = text });
        if (Diaries.Count > MaxDiaries) Diaries.RemoveRange(0, Diaries.Count - MaxDiaries);
    }

    public void Forget(Pet pet)
    {
        Days.RemoveAll(d => d.PetId == pet.Id);
        Diaries.RemoveAll(d => d.PetId == pet.Id);
    }
}
