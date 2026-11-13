using System.Text.Json;
using System.Text.Json.Serialization;
using PetFam.Core.Pets;

namespace PetFam.Core.Persistence;

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

    // Growth (plan §4.6). Null in saves from before growth existed: those pets are adults.
    public int? Days { get; set; }
    public DateTime AdoptedUtc { get; set; }
    public DateOnly ActiveDate { get; set; }
    public float ActiveMinutes { get; set; }
    public bool CountedToday { get; set; }
    public LifeStage AnnouncedStage { get; set; }
    public int LastAnniversary { get; set; }

    // Moving between virtual desktops (plan §6.4.3). Null in older saves: the species default.
    public TravelMode? Travel { get; set; }
    public string? HomeCellId { get; set; }

    /// <summary>Breed id. Null in older saves: the grey tabby is a Korean shorthair, the orange dog a Pomeranian.</summary>
    public string? Breed { get; set; }

    /// <summary>Colours the user picked (0xRRGGBB); null: the breed's colour.</summary>
    public uint? CoatFur { get; set; }
    public uint? CoatLight { get; set; }
    public uint? CoatMarking { get; set; }
    public uint? CoatEyes { get; set; }

    /// <summary>What the pet wears. Null in older saves: nothing.</summary>
    public List<OutfitPieceSave>? Outfit { get; set; }
    public bool DressForWeather { get; set; }
}

public sealed class OutfitPieceSave
{
    public OutfitSlot Slot { get; set; }
    public string Item { get; set; } = "";
    public uint Color { get; set; }
}

public sealed class RelationSave
{
    public Guid A { get; set; }
    public Guid B { get; set; }
    public float Friendship { get; set; }
    public float Dominance { get; set; }
}

public sealed class HouseSave
{
    public Guid Id { get; set; }
    public string CellId { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public string Name { get; set; } = "";
    public World.HouseShape Shape { get; set; }
    public int RoofColor { get; set; }
    public int WallColor { get; set; }
    public List<string> Decorations { get; set; } = new();

    /// <summary>The pet whose house it is; null for everybody's house.</summary>
    public Guid? OwnerId { get; set; }
}

public sealed class SaveData
{
    public int Version { get; set; } = 1;
    public DateTime SavedAtUtc { get; set; }
    public List<PetSave> Pets { get; set; } = new();
    public List<RelationSave> Relations { get; set; } = new();
    public List<HouseSave> Houses { get; set; } = new();

    /// <summary>"en" or "ko". Null: English (the default).</summary>
    public string? Language { get; set; }

    /// <summary>Where to get the real weather for (null: the default city).</summary>
    public WeatherSettingsSave? Weather { get; set; }
}

public sealed class WeatherSettingsSave
{
    public string Place { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>False: the user turned real weather off (always clear).</summary>
    public bool Enabled { get; set; } = true;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SaveData))]
internal partial class SaveJsonContext : JsonSerializerContext
{
}

public static class SaveStore
{
    public static SaveData ToSave(IEnumerable<Pet> pets, DateTime nowUtc, Social.SocialState? social = null, IEnumerable<World.Cell>? cells = null) => new()
    {
        SavedAtUtc = nowUtc,
        Houses = cells?.SelectMany(c => c.Houses.Select(h => new HouseSave
        {
            Id = h.Id, CellId = c.Id, X = h.Position.X, Y = h.Position.Y, Name = h.Name, Shape = h.Shape,
            RoofColor = h.RoofColor, WallColor = h.WallColor, Decorations = h.Decorations.ToList(), OwnerId = h.OwnerId,
        })).ToList() ?? new(),
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
            Days = p.Growth.Days,
            AdoptedUtc = p.Growth.AdoptedUtc,
            ActiveDate = p.Growth.ActiveDate,
            ActiveMinutes = p.Growth.ActiveMinutes,
            CountedToday = p.Growth.CountedToday,
            AnnouncedStage = p.Growth.AnnouncedStage,
            LastAnniversary = p.Growth.LastAnniversary,
            Travel = p.Travel,
            HomeCellId = p.HomeCellId,
            Breed = p.Breed.Id,
            CoatFur = p.Coat?.Fur,
            CoatLight = p.Coat?.Light,
            CoatMarking = p.Coat?.Marking,
            CoatEyes = p.Coat?.Eyes,
            Outfit = p.Outfit.Pieces.Count == 0 ? null
                : p.Outfit.Pieces.Select(kv => new OutfitPieceSave { Slot = kv.Key, Item = kv.Value.ItemId, Color = kv.Value.Color }).ToList(),
            DressForWeather = p.Outfit.DressForWeather,
        }).ToList(),
    };

    public static Pet Restore(PetSave s, DateTime nowUtc, Random rng)
    {
        var pet = new Pet(s.Species, s.Name, nowUtc, rng, Breed.Find(s.Breed, s.Species))
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
        pet.Growth = s.Days is { } days
            ? new Growth(s.AdoptedUtc == default ? nowUtc : s.AdoptedUtc, days)
            {
                ActiveDate = s.ActiveDate,
                ActiveMinutes = s.ActiveMinutes,
                CountedToday = s.CountedToday,
                AnnouncedStage = s.AnnouncedStage,
                LastAnniversary = s.LastAnniversary,
            }
            : new Growth(nowUtc.AddDays(-Growth.AdultAt), Growth.AdultAt);   // pets from before growth: adults
        if (s.Travel is { } travel) pet.Travel = travel;
        pet.HomeCellId = s.HomeCellId;
        pet.Coat = new PetCoat(s.CoatFur, s.CoatLight, s.CoatMarking, s.CoatEyes).OrNull();
        pet.Outfit = new Outfit { DressForWeather = s.DressForWeather };
        foreach (var o in s.Outfit ?? new())
            if (OutfitCatalog.Find(o.Item) is { } item && item.Slot == o.Slot) pet.Outfit.Set(o.Slot, new OutfitPiece(o.Item, o.Color));
        return pet;
    }

    public static World.PetHouse Restore(HouseSave s) => new()
    {
        Id = s.Id, Position = new Geometry.Vec2(s.X, s.Y), Name = s.Name, Shape = s.Shape,
        RoofColor = s.RoofColor, WallColor = s.WallColor, OwnerId = s.OwnerId,
        Decorations = s.Decorations.Where(d => World.PetHouse.AllDecorations.Contains(d)).ToHashSet(),
    };

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
