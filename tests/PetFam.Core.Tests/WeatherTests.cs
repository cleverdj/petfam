using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class WeatherTests
{
    private static readonly DateTime T0 = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

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

    private static (Simulation Sim, Cell Cell) Make()
    {
        var sim = new Simulation(new FakePlatform(), new Random(3));
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        var cell = sim.World.Upsert(new Cell
        {
            Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = bounds, WorkingArea = working,
            Map = HabitatMap.Flat(bounds, working),
        });
        return (sim, cell);
    }

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += 0.1f) sim.Tick(0.1f, T0);
    }

    [Theory]
    [InlineData(0, WeatherKind.Clear)]
    [InlineData(3, WeatherKind.Cloudy)]
    [InlineData(45, WeatherKind.Fog)]
    [InlineData(53, WeatherKind.Rain)]
    [InlineData(65, WeatherKind.Rain)]
    [InlineData(81, WeatherKind.Rain)]
    [InlineData(73, WeatherKind.Snow)]
    [InlineData(86, WeatherKind.Snow)]
    [InlineData(95, WeatherKind.Thunder)]
    public void Weather_codes_map_to_what_the_desktop_shows(int code, WeatherKind kind)
    {
        var w = WeatherNow.FromWmo(code, 10f, true, "Seoul", T0);
        Assert.Equal(kind, w.Kind);
        Assert.StartsWith("Seoul · ", w.Describe());
    }

    [Fact]
    public void Heavier_rain_codes_mean_heavier_rain()
    {
        Assert.True(WeatherNow.FromWmo(65, 10f, true, "", T0).Intensity > WeatherNow.FromWmo(61, 10f, true, "", T0).Intensity);
        Assert.Equal("Heavy rain 10°C", WeatherNow.FromWmo(65, 10f, true, "", T0).Describe());
    }

    [Fact]
    public void A_cat_runs_into_its_house_when_it_rains_and_comes_out_when_it_stops()
    {
        var (sim, cell) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Momo", cell, T0);
        cat.Growth = new Growth(T0, Growth.AdultAt);
        var house = new PetHouse { Position = new Vec2(cat.Position.X + 300, cat.Position.Y) };
        Assert.True(sim.AddHouse(cell, house));

        sim.SetWeather(WeatherNow.Pretend(WeatherKind.Rain, 0.7f));
        for (var i = 0; i < 400 && !cat.Hidden; i++) sim.Tick(0.1f, T0);
        Assert.True(cat.Hidden);
        Assert.IsType<ShelterBehavior>(cat.Engine.Current);
        Assert.Equal(house.Id, cat.InsideHouse);
        var wetInside = cat.Appearance.Wet;
        Run(sim, 20f);
        Assert.True(cat.Appearance.Wet <= wetInside + 1e-4f);     // no more rain on it inside

        sim.SetWeather(WeatherNow.Pretend(WeatherKind.Clear, 0f));
        for (var i = 0; i < 300 && cat.Engine.Current is ShelterBehavior; i++) sim.Tick(0.1f, T0);
        Assert.False(cat.Hidden);
        Assert.Null(cat.InsideHouse);
    }

    [Fact]
    public void Pets_only_go_into_their_own_house_or_everybodys()
    {
        var (sim, cell) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Nabi", cell, T0);
        var dog = sim.AddPet(PetSpecies.Dog, "Happy", cell, T0);
        cat.Growth = new Growth(T0, Growth.AdultAt);
        var dogs = new PetHouse { Name = "Happy's", OwnerId = dog.Id, Position = new Vec2(cat.Position.X + 200, cat.Position.Y) };
        var cats = new PetHouse { Name = "Nabi's", OwnerId = cat.Id, Position = new Vec2(cat.Position.X - 400, cat.Position.Y) };
        Assert.True(sim.AddHouse(cell, dogs));
        Assert.True(sim.AddHouse(cell, cats));
        Assert.True(dogs.Welcomes(dog));
        Assert.False(dogs.Welcomes(cat));

        // The dog's house is nearer, but the cat runs to its own.
        sim.SetWeather(WeatherNow.Pretend(WeatherKind.Rain, 0.7f));
        for (var i = 0; i < 600 && !cat.Hidden; i++) sim.Tick(0.1f, T0);
        Assert.Equal(cats.Id, cat.InsideHouse);

        // With only the dog's house there, the cat does not go in (it curls up instead).
        sim.SetWeather(WeatherNow.Pretend(WeatherKind.Clear, 0f));
        Run(sim, 30f);
        sim.RemoveHouse(cell, cats);
        sim.SetWeather(WeatherNow.Pretend(WeatherKind.Rain, 0.7f));
        Run(sim, 40f);
        Assert.NotEqual(dogs.Id, cat.InsideHouse);

        // Saying goodbye to the dog makes its house everybody's.
        Assert.True(sim.RemovePet(dog));
        Assert.Null(dogs.OwnerId);
        Assert.True(dogs.Welcomes(cat));
    }

    [Fact]
    public void A_house_remembers_whose_it_is()
    {
        var (sim, cell) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Nabi", cell, T0);
        cell.Houses.Add(new PetHouse { Name = "Nabi's", OwnerId = cat.Id });
        var save = PetFam.Core.Persistence.SaveStore.ToSave(sim.Pets, T0, null, sim.World.Cells);
        Assert.Equal(cat.Id, PetFam.Core.Persistence.SaveStore.Restore(save.Houses[0]).OwnerId);
        save.Houses[0].OwnerId = null;                             // houses from before owners: everybody's
        Assert.Null(PetFam.Core.Persistence.SaveStore.Restore(save.Houses[0]).OwnerId);
    }

    [Fact]
    public void A_cat_on_shady_floor_still_goes_home_when_it_rains()
    {
        var (sim, cell) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Nabi", cell, T0);
        cat.Growth = new Growth(T0, Growth.AdultAt);
        // A patch of "shelter" recognised in the wallpaper (a dark area) right where the cat stands.
        cell.Map.Zones.Add(new Zone { Type = ZoneType.Hide, Bounds = new RectI((int)cat.Position.X - 150, (int)cat.Position.Y - 150, 300, 300) });
        var house = new PetHouse { OwnerId = cat.Id, Position = new Vec2(cat.Position.X + 400, cat.Position.Y) };
        Assert.True(sim.AddHouse(cell, house));
        sim.SetWeather(WeatherNow.Pretend(WeatherKind.Rain, 0.25f));
        for (var i = 0; i < 600 && !cat.Hidden; i++) sim.Tick(0.1f, T0);
        Assert.Equal(house.Id, cat.InsideHouse);
    }

    [Fact]
    public void Pets_out_in_the_rain_get_wet_and_dogs_do_not_mind_light_rain()
    {
        var (sim, cell) = Make();
        var dog = sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);
        sim.SetWeather(WeatherNow.Pretend(WeatherKind.Rain, 0.5f));
        Assert.Equal(0f, ShelterBehavior.Dislike(dog, sim.Weather));
        Run(sim, 60f);
        Assert.True(dog.Appearance.Wet > 0.2f);
        Assert.IsNotType<ShelterBehavior>(dog.Engine.Current);

        sim.SetWeather(WeatherNow.Pretend(WeatherKind.Thunder, 0.9f));
        Assert.True(ShelterBehavior.Dislike(dog, sim.Weather) > 0.5f);   // but thunder scares dogs too
    }

    [Fact]
    public void Snow_piles_up_while_it_snows_and_dogs_love_it()
    {
        var (sim, cell) = Make();
        var dog = sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);
        dog.Growth = new Growth(T0, Growth.AdultAt);
        dog.Needs.Fun = 20f;
        sim.SetWeather(WeatherNow.Pretend(WeatherKind.Snow, 1f));
        Assert.Contains(dog.Bubble, Breed.Pomeranian.Voice.Snow.Select(l => l.Text));
        Run(sim, 120f);
        Assert.True(sim.SnowCover > 0.15f);
        Assert.True(dog.Needs.Fun > 20f, $"fun {dog.Needs.Fun}, doing {dog.Engine.Current?.Name}");

        sim.SetWeather(WeatherNow.Pretend(WeatherKind.Clear, 0f));
        var cover = sim.SnowCover;
        Run(sim, 60f);
        Assert.True(sim.SnowCover < cover);                       // melting
    }

    [Fact]
    public void Thunder_flashes_lightning_now_and_then()
    {
        var (sim, cell) = Make();
        sim.AddPet(PetSpecies.Cat, "Momo", cell, T0);
        sim.SetWeather(WeatherNow.Pretend(WeatherKind.Thunder, 1f));
        var flashes = 0;
        var last = sim.SinceLightning;
        for (var i = 0; i < 1200; i++)
        {
            sim.Tick(0.1f, T0);
            if (sim.SinceLightning < last) flashes++;
            last = sim.SinceLightning;
        }
        Assert.InRange(flashes, 2, 25);
    }
}
