using System.Text.Json;
using System.Text.Json.Serialization;
using ZooZoo.Core.Pets;

namespace ZooZoo.Core.Persistence;

public sealed class PetSave
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public PetSpecies Species { get; set; }

    public float Hunger { get; set; }
    public float Energy { get; set; }
    public float Fun { get; set; }
    public float Affection { get; set; }
    public float Curiosity { get; set; }

    public float Bond { get; set; }
    public DateTime LastInteractionUtc { get; set; }
    public DateTime LastDailyPresenceUtc { get; set; }
    public DateOnly PetCounterDate { get; set; }
    public int PetCountToday { get; set; }

    public float Playfulness { get; set; }
    public float Shyness { get; set; }
    public float Activity { get; set; }
    public float Homebody { get; set; }
    public float Tidiness { get; set; }

    public float Wet { get; set; }
    public float Mud { get; set; }
    public bool MudDried { get; set; }

    public string CellId { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class RelationSave
{
    public Guid A { get; set; }
    public Guid B { get; set; }
    public float Friendship { get; set; }
    public float Dominance { get; set; }
}

public sealed class SaveData
{
    public int Version { get; set; } = 1;
    public DateTime SavedAtUtc { get; set; }
    public List<PetSave> Pets { get; set; } = new();
    public List<RelationSave> Relations { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SaveData))]
internal partial class SaveJsonContext : JsonSerializerContext
{
}

public static class SaveStore
{
    public static SaveData ToSave(IEnumerable<Pet> pets, DateTime nowUtc, Social.SocialState? social = null) => new()
    {
        SavedAtUtc = nowUtc,
        Relations = social?.All.Select(r => new RelationSave { A = r.A, B = r.B, Friendship = r.Friendship, Dominance = r.Dominance }).ToList() ?? new(),
        Pets = pets.Select(p => new PetSave
        {
            Id = p.Id,
            Name = p.Name,
            Species = p.Species,
            Hunger = p.Needs.Hunger,
            Energy = p.Needs.Energy,
            Fun = p.Needs.Fun,
            Affection = p.Needs.Affection,
            Curiosity = p.Needs.Curiosity,
            Bond = p.Bond.Value,
            LastInteractionUtc = p.Bond.LastInteractionUtc,
            LastDailyPresenceUtc = p.Bond.LastDailyPresenceUtc,
            PetCounterDate = p.Bond.PetCounterDate,
            PetCountToday = p.Bond.PetCountToday,
            Playfulness = p.Personality.Playfulness,
            Shyness = p.Personality.Shyness,
            Activity = p.Personality.Activity,
            Homebody = p.Personality.Homebody,
            Tidiness = p.Personality.Tidiness,
            Wet = p.Appearance.Wet,
            Mud = p.Appearance.Mud,
            MudDried = p.Appearance.MudDried,
            CellId = p.CellId,
            X = p.Position.X,
            Y = p.Position.Y,
        }).ToList(),
    };

    public static Pet Restore(PetSave s, DateTime nowUtc, Random rng)
    {
        var pet = new Pet(s.Species, s.Name, nowUtc, rng)
        {
            Id = s.Id,
            Personality = new Personality
            {
                Playfulness = s.Playfulness,
                Shyness = s.Shyness,
                Activity = s.Activity,
                Homebody = s.Homebody,
                Tidiness = s.Tidiness,
            },
            CellId = s.CellId,
            Position = new Geometry.Vec2(s.X, s.Y),
        };
        pet.Needs.Hunger = s.Hunger;
        pet.Needs.Energy = s.Energy;
        pet.Needs.Fun = s.Fun;
        pet.Needs.Affection = s.Affection;
        pet.Needs.Curiosity = s.Curiosity;
        pet.Bond.Restore(s.Bond, s.LastInteractionUtc, s.LastDailyPresenceUtc, s.PetCounterDate, s.PetCountToday);
        pet.Appearance.Restore(s.Wet, s.Mud, s.MudDried);
        return pet;
    }

    public static void Write(string path, SaveData data)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, SaveJsonContext.Default.SaveData));
        File.Move(tmp, path, overwrite: true);
    }

    public static SaveData? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), SaveJsonContext.Default.SaveData);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
