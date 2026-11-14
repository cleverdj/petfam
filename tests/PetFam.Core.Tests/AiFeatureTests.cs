using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class AiFeatureTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 3, 0, 0, DateTimeKind.Utc);

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
        var sim = new Simulation(new FakePlatform(), new Random(4));
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        var cell = sim.World.Upsert(new Cell
        {
            Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = bounds, WorkingArea = working,
            Map = HabitatMap.Flat(bounds, working),
        });
        return (sim, cell);
    }

    [Fact]
    public void An_ai_labelled_grid_paints_the_matching_parts_of_the_screen()
    {
        var bounds = new RectI(0, 0, 1600, 900);
        var flat = HabitatEdits.FlatMask(HabitatMap.Flat(bounds, bounds));
        var strokes = HabitatEdits.FromGrid(new[] { "KKKK", "BBBB", "GGWW", "SSUH" }, 900f / 1600f);

        Assert.Equal(16, strokes.Count);
        var mask = HabitatEdits.Apply(flat, strokes);
        ZoneType At(float x, float y) => mask.Kind((int)(x * mask.Cols), (int)(y * mask.Rows));
        Assert.Equal(ZoneType.Sky, At(0.5f, 0.1f));
        Assert.Equal(ZoneType.Ground, At(0.1f, 0.6f));
        Assert.Equal(ZoneType.Water, At(0.9f, 0.6f));
        Assert.Equal(ZoneType.Sand, At(0.1f, 0.9f));
        Assert.Equal(ZoneType.Hide, At(0.9f, 0.9f));
        Assert.Empty(HabitatEdits.FromGrid(new[] { "??", "--" }, 0.5f));          // unknown letters paint nothing
    }

    [Fact]
    public void The_day_is_kept_for_the_diary_what_it_did_who_with_and_what_the_owner_did()
    {
        var (sim, cell) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Momo", cell, T0);
        var dog = sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);
        sim.Social.Get(cat, dog).Friendship = 80f;
        sim.Tick(0.1f, T0);

        sim.Feed(cat, T0);
        sim.BeginCarry(cat);
        sim.EndCarry(cat, cat.Position);
        Assert.True(sim.StartPlay(dog, cat));
        for (var i = 0; i < 50; i++) sim.Tick(0.1f, T0.AddSeconds(i * 0.1));

        var day = sim.Journal.Day(cat, Talk.Journal.Today(T0));
        Assert.Equal(1, day.Owner["fed"]);
        Assert.True(day.Owner.ContainsKey("carried"));
        Assert.Contains(day.With, w => w.StartsWith("Peanut: "));
        var text = day.Describe(_ => null);
        Assert.Contains("The owner: ", text);

        sim.Journal.Write(cat, Talk.Journal.Today(T0), "Today I napped.");
        Assert.Equal("Today I napped.", sim.Journal.DiariesOf(cat).First().Text);
        Assert.True(sim.RemovePet(cat));
        Assert.Empty(sim.Journal.DiariesOf(cat));                               // gone with the pet
    }

    [Fact]
    public void The_coding_agent_buddy_reacts_but_not_to_every_single_step()
    {
        var (sim, cell) = Make();
        var dog = sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);
        sim.Tick(0.1f, T0);

        Assert.Same(dog, sim.AgentEvent("done"));
        Assert.NotNull(dog.Bubble);
        Assert.Null(sim.AgentEvent("working"));                                 // it just spoke
        Assert.Same(dog, sim.AgentEvent("waiting"));                            // being needed always gets through
        Assert.Null(sim.AgentEvent("nonsense"));
    }
}
