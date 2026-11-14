using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PetFam.Core.Pets;
using PetFam.Core.Social;
using PetFam.Core.World;

namespace PetFam.Core.Talk;

/// <summary>
/// Something that writes text from a prompt: the AI the user already has (a CLI signed in to their subscription,
/// a local model, …). Runs off the simulation thread; null or an exception means "no answer".
/// </summary>
public interface IDialogueWriter
{
    /// <summary>Shown in the log and the tray ("Claude").</summary>
    string Name { get; }

    Task<string?> WriteAsync(string system, string prompt, CancellationToken ct);

    /// <summary>Can it look at a picture (a wallpaper, a photo of the user's pet)?</summary>
    bool CanSee => false;

    /// <summary>Like WriteAsync, with an image file to look at. Only when CanSee.</summary>
    Task<string?> SeeAsync(string system, string prompt, string imagePath, CancellationToken ct) => Task.FromResult<string?>(null);
}

public sealed record DialogueLine(Pet Speaker, string Text, string? Act);

/// <summary>A conversation to act out, and how it left the two feeling about each other (-1 tense … +1 warm).</summary>
public sealed record Dialogue(Pet A, Pet B, IReadOnlyList<DialogueLine> Lines, int Tone);

/// <summary>
/// Builds the prompt for a conversation between two pets, each in its own persona (breed temperament, personality,
/// age, how it feels right now, what it wears, how the two get on), and reads the answer back. Only what happens in
/// the pets' world goes into the prompt, never anything from the user's screen.
/// </summary>
public static class PetTalk
{
    public const string System =
        "You write the lines of pets in a cozy desktop pet game. Each pet has its own personality and voice. Reply with JSON only.";

    public const int MinLines = 2;
    public const int MaxLines = 6;

    /// <summary>A speech bubble holds about this much.</summary>
    public static int MaxChars => L.Ko ? 34 : 64;

    private static readonly string[] DogActs = { "sit", "lie", "idle", "playbow" };
    private static readonly string[] CatActs = { "sit", "lie", "idle", "hiss", "groom", "wiggle" };

    public static IReadOnlyList<string> ActsFor(PetSpecies species) => species == PetSpecies.Cat ? CatActs : DogActs;

    public static string Prompt(Pet a, Pet b, SocialState social, WeatherNow weather, DayPhase phase, IReadOnlyList<string>? lastTime = null,
                                Scenery? scene = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Two pets who live together on their owner's computer desktop meet and talk.");
        sb.AppendLine();
        sb.AppendLine("A:");
        sb.AppendLine(Persona(a));
        sb.AppendLine();
        sb.AppendLine("B:");
        sb.AppendLine(Persona(b));
        sb.AppendLine();
        sb.AppendLine("Between them: " + Relation(a, b, social));
        sb.AppendLine($"Now: {PhaseText(phase)}, {WeatherText(weather)}. A is {Doing(a)}; B is {Doing(b)}.");
        if (lastTime is { Count: > 0 })
            sb.AppendLine("Last time they talked: " + string.Join(" / ", lastTime) + " (don't repeat it; it may carry on from it).");
        sb.AppendLine(SceneText(scene, two: true));
        sb.AppendLine();
        sb.AppendLine($"Write their conversation: {MinLines + 1} to {MaxLines} lines, A speaks first, mostly taking turns.");
        sb.AppendLine(Language);
        sb.AppendLine($"Each line at most {MaxChars} characters. Keep to their little world: the place they live in, the owner, food, naps, " +
                      "their house, the weather, toys, outfits, each other. Stay in character: a cat and a dog sound different, " +
                      "and each pet's personality, age and mood show. Pet sounds are welcome. Only the spoken words: no stage directions or actions in brackets (use act), no emoji. " + NeverMention);
        sb.AppendLine("Reply with JSON only, like:");
        sb.AppendLine("{\"lines\":[{\"who\":\"A\",\"text\":\"...\",\"act\":\"sit\"},{\"who\":\"B\",\"text\":\"...\"}],\"tone\":\"warm\"}");
        sb.AppendLine($"act is optional, what the speaker does while saying it: for A one of {string.Join(", ", ActsFor(a.Species))}; " +
                      $"for B one of {string.Join(", ", ActsFor(b.Species))}.");
        sb.AppendLine("tone is how the talk left them feeling about each other: warm, neutral or tense.");
        return sb.ToString();
    }

    private static string Language => L.Ko
        ? "Language: Korean, casual speech (반말) the way pets would talk. Call the owner 집사."
        : "Language: English, casual. Call the owner \"my human\".";

    private const string NeverMention =
        "Never mention AI, apps, computers, screens or wallpapers: to the pets the scenery is simply the place they live in.";

    /// <summary>
    /// The place they live in (the wallpaper, as the AI described it). Just changed: they notice, say what is new, and
    /// may compare it with the old place. Otherwise it is there to mention when it fits.
    /// </summary>
    public static string SceneText(Scenery? scene, bool two)
    {
        if (scene is null) return "";
        string their = two ? "Their" : "Its", they = two ? "they" : "it", live = two ? "live" : "lives", talk = two ? "talk" : "talks",
               notice = two ? "notice" : "notices", may = two ? "They may" : "It may";
        if (scene is { Fresh: true, Before: { } before })
            return $"{their} world has just changed! Before, {they} lived in: {before}. Now {they} {live} in: {scene.Now}. " +
                   $"This is what {they} {talk} about: what {they} {notice} in the new place (name real things in it), how it feels, " +
                   "and whether the old place was better or the new one is (honest and in character: missing the old place is fine).";
        return $"The place {they} {live} in (the scenery around {(two ? "them" : "it")}): {scene.Now}. " +
               $"{may} mention something there (a mountain, the water, a tree, the light) when it fits.";
    }

    /// <summary>One pet thinking out loud, mostly about the place around it (one or two short lines).</summary>
    public static string RemarkPrompt(Pet pet, WeatherNow weather, DayPhase phase, Scenery? scene)
    {
        var sb = new StringBuilder();
        sb.AppendLine("A pet who lives on its owner's computer desktop says something to itself.");
        sb.AppendLine();
        sb.AppendLine(Persona(pet));
        sb.AppendLine($"Now: {PhaseText(phase)}, {WeatherText(weather)}. It is {Doing(pet)}.");
        sb.AppendLine(SceneText(scene, two: false));
        sb.AppendLine();
        sb.AppendLine("Write what it says: 1 or 2 short lines, about the place around it (something it sees there), in character.");
        sb.AppendLine(Language);
        sb.AppendLine($"Each line at most {MaxChars} characters. Pet sounds welcome. Only the spoken words: no stage directions, no emoji. " + NeverMention);
        sb.AppendLine("Reply with JSON only, like: {\"lines\":[\"...\"],\"act\":\"sit\"}");
        sb.AppendLine($"act is optional, what it does while saying it: one of {string.Join(", ", ActsFor(pet.Species))}.");
        return sb.ToString();
    }

    /// <summary>The lines of a remark, or null if the answer isn't usable.</summary>
    public static (IReadOnlyList<string> Lines, string? Act)? ParseRemark(string? raw, Pet pet)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var root = doc.RootElement;
            if (!root.TryGetProperty("lines", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
            var lines = arr.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? StripDirections(e.GetString())
                           : e.ValueKind == JsonValueKind.Object && e.TryGetProperty("text", out var t) ? StripDirections(t.GetString()) : null)
                .Where(t => !string.IsNullOrEmpty(t) && t.Length <= MaxChars * 2)
                .Take(2).Select(t => t!).ToList();
            if (lines.Count == 0) return null;
            var act = root.TryGetProperty("act", out var ac) && ac.ValueKind == JsonValueKind.String ? ac.GetString()?.Trim().ToLowerInvariant() : null;
            if (act is not null && !ActsFor(pet.Species).Contains(act)) act = null;
            return (lines, act);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Who the pet is: breed temperament, personality, age, how it feels now and what it wears.</summary>
    public static string Persona(Pet p)
    {
        var b = p.Breed;
        var species = p.Species == PetSpecies.Cat ? "cat" : "dog";
        var sb = new StringBuilder();
        sb.Append($"{p.Name}, a {Age(p.Growth.Stage)} {b.Name.En} ({species}). {b.Persona.En}");

        var traits = new List<string>();
        void Trait(float v, string high, string low)
        {
            if (v >= 0.65f) traits.Add(high);
            else if (v <= 0.35f) traits.Add(low);
        }
        var pp = p.Personality;
        Trait(pp.Playfulness, "very playful", "calm and not into games");
        Trait(pp.Shyness, "shy", "outgoing");
        Trait(pp.Activity, "full of energy", "lazy");
        Trait(pp.Homebody, "a homebody", "adventurous");
        Trait(pp.Tidiness, "fussy about being clean", "doesn't mind getting dirty");
        if (traits.Count > 0) sb.Append(" Personality: " + string.Join(", ", traits) + ".");
        sb.Append(p.Growth.Stage switch
        {
            LifeStage.Baby => " Still a baby: short, babyish, clumsy words.",
            LifeStage.Junior => " Young and excitable.",
            LifeStage.Senior => " Old: slow, wise, a bit grumpy or gentle.",
            _ => "",
        });

        var now = new List<string>();
        now.Add(MoodCalc.Level(p) switch { MoodLevel.Good => "in a good mood", MoodLevel.Bad => "in a bad mood", _ => "in an okay mood" });
        if (p.Needs.Hunger < 35f) now.Add("hungry");
        if (p.Needs.Energy < 35f) now.Add("sleepy");
        if (p.Needs.Fun < 30f) now.Add("bored");
        if (p.Needs.Affection < 30f) now.Add("missing the owner's attention");
        if (p.Appearance.Wet > 0.3f) now.Add("wet");
        if (p.Appearance.Mud > 0.3f) now.Add("muddy");
        sb.Append(" Right now: " + string.Join(", ", now) + ".");

        var wears = Enum.GetValues<OutfitSlot>().Select(s => p.Wears(s)?.Item?.Name.En).Where(n => n is not null).ToList();
        if (wears.Count > 0) sb.Append(" Wearing: " + string.Join(", ", wears) + ".");
        sb.Append(" Bond with the owner: " + p.Bond.Stage.ToString().ToLowerInvariant() + ".");

        var voice = b.Voice.Idle.Concat(b.Voice.Happy).Take(3).Select(l => "\"" + l.Text + "\"").ToList();
        if (voice.Count > 0) sb.Append(" Talks like: " + string.Join(" ", voice));
        return sb.ToString();
    }

    private static string Age(LifeStage s) => s switch
    {
        LifeStage.Baby => "baby",
        LifeStage.Junior => "young",
        LifeStage.Senior => "senior",
        _ => "grown-up",
    };

    private static string Relation(Pet a, Pet b, SocialState social)
    {
        var f = social.Friendship(a, b);
        var text = f >= 70f ? "close friends" : f >= 45f ? "friendly" : f >= 25f ? "not close yet" : "they don't get along";
        var dom = social.DominanceOf(a, b);
        if (dom > 0.15f) text += $"; {a.Name} is the boss of the two";
        else if (dom < -0.15f) text += $"; {b.Name} is the boss of the two";
        var matching = OutfitCatalog.Matching(a, b);
        if (matching > 0) text += "; they wear matching outfits";
        return text + ".";
    }

    private static string PhaseText(DayPhase phase) => phase switch
    {
        DayPhase.Night => "night",
        DayPhase.Dawn => "early morning",
        DayPhase.Dusk => "evening",
        _ => "daytime",
    };

    private static string WeatherText(WeatherNow w) => w.Kind.ToString().ToLowerInvariant() switch
    {
        "clear" => "clear sky",
        "heavyrain" => "pouring rain",
        "thunder" => "a thunderstorm",
        var k => k,
    };

    private static string Doing(Pet p) => p.Animation switch
    {
        "sit" => "sitting",
        "lie" => "lying down",
        "groom" => "grooming",
        "walk" or "run" => "walking about",
        _ => "standing about",
    };

    /// <summary>"(wags its tail) Hi!" → "Hi!": stage directions don't belong in a speech bubble.</summary>
    public static string? StripDirections(string? text)
    {
        if (text is null) return null;
        var s = Regex.Replace(text, @"\([^)]*\)|\*[^*]*\*|\[[^\]]*\]", " ");
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }

    /// <summary>The conversation from the AI's answer, or null if it isn't usable. Tolerates text around the JSON.</summary>
    public static Dialogue? Parse(string? raw, Pet a, Pet b)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var root = doc.RootElement;
            if (!root.TryGetProperty("lines", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
            var lines = new List<DialogueLine>();
            foreach (var el in arr.EnumerateArray())
            {
                if (lines.Count >= MaxLines) break;
                var who = el.TryGetProperty("who", out var w) ? w.GetString() : null;
                var speaker = who?.Trim().ToUpperInvariant() switch { "A" => a, "B" => b, _ => null };
                if (speaker is null && who is not null)
                    speaker = string.Equals(who.Trim(), a.Name, StringComparison.OrdinalIgnoreCase) ? a
                            : string.Equals(who.Trim(), b.Name, StringComparison.OrdinalIgnoreCase) ? b : null;
                var text = el.TryGetProperty("text", out var t) ? StripDirections(t.GetString()) : null;
                if (speaker is null || string.IsNullOrEmpty(text)) continue;
                if (text.Length > MaxChars * 2) continue;                      // rambling: not bubble material
                var act = el.TryGetProperty("act", out var ac) ? ac.GetString()?.Trim().ToLowerInvariant() : null;
                if (act is not null && !ActsFor(speaker.Species).Contains(act)) act = null;
                lines.Add(new DialogueLine(speaker, text, act));
            }
            if (lines.Count < MinLines) return null;
            var tone = root.TryGetProperty("tone", out var tn) ? tn.GetString()?.Trim().ToLowerInvariant() : null;
            return new Dialogue(a, b, lines, tone switch { "warm" => 1, "tense" => -1, _ => 0 });
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
