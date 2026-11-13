using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Persistence;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class OutfitTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private static Pet Make(Breed breed, float bond)
    {
        var pet = new Pet(breed.Species, "t", T0, new Random(3), breed) { Growth = new Growth(T0, Growth.AdultAt) };
        pet.Bond.DebugSet(bond, T0);
        return pet;
    }

    [Fact]
    public void Every_slot_has_pieces_and_more_unlock_as_the_bond_grows()
    {
        foreach (var slot in Enum.GetValues<OutfitSlot>()) Assert.NotEmpty(OutfitCatalog.Of(slot));
        Assert.Equal(OutfitCatalog.All.Count, OutfitCatalog.All.Select(i => i.Id).Distinct().Count());
        Assert.All(OutfitCatalog.All, i => Assert.NotEmpty(i.Colors));

        int Unlocked(float bond) { var p = Make(Breed.Pomeranian, bond); return OutfitCatalog.All.Count(i => OutfitCatalog.IsUnlocked(p, i)); }
        Assert.True(Unlocked(0) >= 1);                         // a collar from day one
        Assert.True(Unlocked(0) < Unlocked(50));
        Assert.True(Unlocked(50) < Unlocked(100));
        Assert.Equal(OutfitCatalog.All.Count, Unlocked(100));

        // Something it already wears stays wearable even below its level.
        var pet = Make(Breed.Pomeranian, 10);
        pet.Outfit.Set(OutfitSlot.Head, new OutfitPiece("crown", 0xE84A5F));
        Assert.True(OutfitCatalog.IsUnlocked(pet, OutfitCatalog.Find("crown")!));
    }

    [Fact]
    public void The_outfit_is_saved_and_old_saves_wear_nothing()
    {
        var pet = Make(Breed.Maltese, 80);
        pet.Outfit.Set(OutfitSlot.Head, new OutfitPiece("straw-hat", 0x3C8DDE));
        pet.Outfit.Set(OutfitSlot.Body, new OutfitPiece("sweater", 0xF2A9C4));
        pet.Outfit.DressForWeather = true;

        var json = System.Text.Json.JsonSerializer.Serialize(SaveStore.ToSave(new[] { pet }, T0));
        var back = SaveStore.Restore(System.Text.Json.JsonSerializer.Deserialize<SaveData>(json)!.Pets[0], T0, new Random(1));
        Assert.Equal(new OutfitPiece("straw-hat", 0x3C8DDE), back.Outfit.Get(OutfitSlot.Head));
        Assert.Equal(new OutfitPiece("sweater", 0xF2A9C4), back.Outfit.Get(OutfitSlot.Body));
        Assert.Null(back.Outfit.Get(OutfitSlot.Neck));
        Assert.True(back.Outfit.DressForWeather);

        var old = SaveStore.ToSave(new[] { Make(Breed.Maltese, 0) }, T0).Pets[0];
        old.Outfit = null;
        Assert.True(SaveStore.Restore(old, T0, new Random(1)).Outfit.IsEmpty);
    }

    private sealed class FakePlatform : IPlatform
    {
        public void RegisterOwnWindow(IntPtr handle) { }
        public ForegroundWindowInfo? GetForegroundWindow() => null;
        public Vec2 GetCursorPosition() => new(-5000, -5000);
        public TimeSpan GetIdleTime() => TimeSpan.Zero;
        public bool IsDoNotDisturb() => false;
        public string? GetWallpaperPath() => null;
        public WallpaperFit GetWallpaperFit() => WallpaperFit.Fill;
        public bool IsDesktopAt(Vec2 screenPoint) => true;
    }

    [Fact]
    public void Pets_that_dress_for_the_weather_put_on_a_raincoat_and_mind_the_rain_less()
    {
        var sim = new Simulation(new FakePlatform(), new Random(2));
        var bounds = new RectI(0, 0, 1920, 1080);
        var cell = sim.World.Upsert(new Cell { Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = bounds, WorkingArea = bounds, Map = HabitatMap.Flat(bounds, bounds) });
        var cat = sim.AddPet(Breed.Persian, "Coco", cell, T0);
        cat.Outfit.Set(OutfitSlot.Head, new OutfitPiece("ribbon", 0xF0508C));
        var rain = WeatherNow.Clear with { Kind = WeatherKind.Rain, Intensity = 0.6f };

        var before = ShelterBehavior.Dislike(cat, rain);
        sim.SetWeather(rain);
        sim.Tick(0.1f, T0);
        Assert.Null(cat.Wears(OutfitSlot.Body));                     // not without asking

        cat.Outfit.DressForWeather = true;
        sim.Tick(0.1f, T0);
        Assert.Equal("raincoat", cat.Wears(OutfitSlot.Body)?.ItemId);
        Assert.Equal("ribbon", cat.Wears(OutfitSlot.Head)?.ItemId);  // its own things stay on
        Assert.True(ShelterBehavior.Dislike(cat, rain) < before * 0.5f);

        sim.SetWeather(WeatherNow.Clear with { Kind = WeatherKind.Snow, Intensity = 0.6f });
        sim.Tick(0.1f, T0);
        Assert.Null(cat.Wears(OutfitSlot.Body));
        Assert.Equal("scarf", cat.Wears(OutfitSlot.Neck)?.ItemId);
        Assert.Equal("ribbon", cat.Wears(OutfitSlot.Head)?.ItemId);   // no beanie over its own ribbon

        sim.SetWeather(WeatherNow.Clear);
        sim.Tick(0.1f, T0);
        Assert.Null(cat.WeatherWear);
    }

    [Fact]
    public void Fashion_lovers_are_delighted_and_proud_breeds_grumble()
    {
        Assert.True(OutfitCatalog.FashionLove(Make(Breed.Maltese, 50)) > 0.7f);
        Assert.True(OutfitCatalog.FashionLove(Make(Breed.Shiba, 50)) < 0.3f);

        var sim = new Simulation(new FakePlatform(), new Random(2));
        var maltese = Make(Breed.Maltese, 50);
        sim.ReactToOutfit(maltese, undressed: false);
        Assert.Contains("pretty", maltese.Bubble);
        var shiba = Make(Breed.Shiba, 50);
        sim.ReactToOutfit(shiba, undressed: false);
        Assert.Equal("shake", shiba.Animation);
    }
}
