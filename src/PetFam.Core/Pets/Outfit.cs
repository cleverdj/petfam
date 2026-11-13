namespace PetFam.Core.Pets;

/// <summary>Where a piece goes on the pet.</summary>
public enum OutfitSlot { Neck, Head, Body, Face }

/// <summary>
/// Something a pet can wear: where it goes, its name, the bond it takes to unlock (pets warm up to dressing up as
/// they trust you), and the colours it comes in (the first is its default).
/// </summary>
public sealed record OutfitItem(string Id, OutfitSlot Slot, Loc Name, float Bond, uint[] Colors);

/// <summary>A piece the pet wears, in one of its colours.</summary>
public sealed record OutfitPiece(string ItemId, uint Color)
{
    public OutfitItem? Item => OutfitCatalog.Find(ItemId);
}

/// <summary>Everything PetFam has to wear. Free: pieces unlock as the bond grows (plan: pet dress-up).</summary>
public static class OutfitCatalog
{
    private static Loc T(string en, string ko) => new(en, ko);

    private static readonly uint[] Bright = { 0xE84A5F, 0x3C8DDE, 0x4CB86A, 0xF2B632, 0xA06AD8, 0xF28AB0, 0x2E3440, 0xF4F1EA };
    private static readonly uint[] Soft = { 0xF2A9C4, 0x9CC8F0, 0xA8DCB4, 0xF6D88A, 0xC9B4EC, 0xF4F1EA, 0xE8C8A8, 0x8E949E };

    public static readonly IReadOnlyList<OutfitItem> All = new[]
    {
        new OutfitItem("collar", OutfitSlot.Neck, T("Collar", "목걸이"), 0f, new uint[] { 0xB86E3C, 0xE84A5F, 0x3C8DDE, 0x4CB86A, 0xF2B632, 0x2E3440 }),
        new OutfitItem("bell", OutfitSlot.Neck, T("Bell collar", "방울 목걸이"), 0f, Bright),
        new OutfitItem("bandana", OutfitSlot.Neck, T("Bandana", "반다나"), 0f, Bright),
        new OutfitItem("bowtie", OutfitSlot.Neck, T("Bow tie", "나비넥타이"), 15f, Bright),
        new OutfitItem("scarf", OutfitSlot.Neck, T("Scarf", "목도리"), 25f, new uint[] { 0xD8484A, 0x3C8DDE, 0xF2B632, 0x4CB86A, 0xF4F1EA, 0x8A5A9A }),

        new OutfitItem("ribbon", OutfitSlot.Head, T("Ribbon", "리본"), 0f, new uint[] { 0xF0508C, 0x5A9CF0, 0xF2B632, 0xE84A5F, 0x4CB86A, 0xF4F1EA, 0xA06AD8 }),
        new OutfitItem("flower", OutfitSlot.Head, T("Flower", "꽃핀"), 5f, new uint[] { 0xF28AB0, 0xF4F1EA, 0xF2B632, 0xE84A5F, 0xA06AD8, 0x5A9CF0 }),
        new OutfitItem("beanie", OutfitSlot.Head, T("Beanie", "비니"), 20f, Bright),
        new OutfitItem("straw-hat", OutfitSlot.Head, T("Straw hat", "밀짚모자"), 45f, new uint[] { 0xE84A5F, 0x3C8DDE, 0x4CB86A, 0x2E3440, 0xF28AB0 }),
        new OutfitItem("crown", OutfitSlot.Head, T("Crown", "왕관"), 70f, new uint[] { 0xE84A5F, 0x3C8DDE, 0x4CB86A, 0xA06AD8 }),

        new OutfitItem("sweater", OutfitSlot.Body, T("Sweater", "스웨터"), 10f, Soft.Concat(new uint[] { 0xD8484A, 0x2E5AA0 }).ToArray()),
        new OutfitItem("vest", OutfitSlot.Body, T("Vest", "조끼"), 30f, Bright),
        new OutfitItem("raincoat", OutfitSlot.Body, T("Raincoat", "우비"), 20f, new uint[] { 0xF6CC2E, 0xE84A5F, 0x3C8DDE, 0x4CB86A, 0xF28AB0 }),

        new OutfitItem("glasses", OutfitSlot.Face, T("Round glasses", "동그란 안경"), 35f, new uint[] { 0x2E2A28, 0x8A5A36, 0xD8B040, 0xE84A5F, 0x3C8DDE }),
        new OutfitItem("sunglasses", OutfitSlot.Face, T("Sunglasses", "선글라스"), 55f, new uint[] { 0x2E2A28, 0xF4F1EA, 0xE84A5F, 0xF2B632, 0x3C8DDE }),
    };

    public static OutfitItem? Find(string? id) => All.FirstOrDefault(i => i.Id == id);

    public static IEnumerable<OutfitItem> Of(OutfitSlot slot) => All.Where(i => i.Slot == slot);

    /// <summary>Unlocked once the bond reaches the item's level (and anything it already wears stays wearable).</summary>
    public static bool IsUnlocked(Pet pet, OutfitItem item) =>
        pet.Bond.Value >= item.Bond || pet.Outfit.Get(item.Slot)?.ItemId == item.Id;

    // Worn by the weather (Outfit.DressForWeather): a raincoat in the rain, a scarf and a beanie in the snow.
    public static readonly OutfitPiece RainWear = new("raincoat", 0xF6CC2E);
    public static readonly OutfitPiece SnowScarf = new("scarf", 0xD8484A);
    public static readonly OutfitPiece SnowHat = new("beanie", 0xD8484A);

    /// <summary>
    /// How much this pet likes dressing up, 0..1: the show-dog and lap breeds love it, the proud and wild ones would
    /// rather not, a hairless Sphynx loves a warm sweater. Tidy, less shy pets like it a little more.
    /// </summary>
    public static float FashionLove(Pet pet)
    {
        var b = pet.Breed.Id switch
        {
            "maltese" or "shih-tzu" or "poodle" or "bichon" or "pomeranian" or "persian" or "ragdoll" or "turkish-angora" or "sphynx" or "chihuahua" => 0.85f,
            "shiba" or "jindo" or "siberian-husky" or "border-collie" or "bengal" or "abyssinian" or "norwegian-forest" => 0.15f,
            _ => 0.5f,
        };
        return Math.Clamp(b + (pet.Personality.Tidiness - 0.5f) * 0.2f - (pet.Personality.Shyness - 0.5f) * 0.2f, 0f, 1f);
    }
}

/// <summary>What a pet wears: at most one piece per slot, and whether it dresses for the weather by itself.</summary>
public sealed class Outfit
{
    private readonly Dictionary<OutfitSlot, OutfitPiece> _pieces = new();

    public IReadOnlyDictionary<OutfitSlot, OutfitPiece> Pieces => _pieces;

    /// <summary>Puts on a raincoat in the rain and a scarf (and a beanie, if the head is bare) in the snow.</summary>
    public bool DressForWeather { get; set; }

    public OutfitPiece? Get(OutfitSlot slot) => _pieces.GetValueOrDefault(slot);

    public void Set(OutfitSlot slot, OutfitPiece? piece)
    {
        if (piece is null) _pieces.Remove(slot);
        else _pieces[slot] = piece;
    }

    public bool IsEmpty => _pieces.Count == 0 && !DressForWeather;

    public Outfit Clone()
    {
        var o = new Outfit { DressForWeather = DressForWeather };
        foreach (var (k, v) in _pieces) o._pieces[k] = v;
        return o;
    }
}
