using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Persistence;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class DesktopTravelTests
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

    /// <summary>Three virtual desktops on one 1920x1080 monitor; the user looks at the first.</summary>
    private static (Simulation Sim, Cell[] Cells) Make()
    {
        var sim = new Simulation(new FakePlatform(), new Random(7));
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        var cells = new[] { "a", "b", "c" }.Select(d => sim.World.Upsert(new Cell
        {
            Id = d + ":m", DesktopId = d, MonitorId = "m", Bounds = bounds, WorkingArea = working,
            Map = HabitatMap.Flat(bounds, working),
        })).ToArray();
        sim.World.DesktopOrder.AddRange(new[] { "a", "b", "c" });
        sim.ShowCell(cells[0], T0);
        return (sim, cells);
    }

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += 0.1f) sim.Tick(0.1f, T0);
    }

    [Fact]
    public void Desktops_have_neighbours_in_os_order()
    {
        var (sim, cells) = Make();
        Assert.Same(cells[1], sim.World.Neighbor(cells[0], +1));
        Assert.Null(sim.World.Neighbor(cells[0], -1));
        Assert.Same(cells[1], sim.World.Neighbor(cells[2], -1));
        sim.World.DesktopNames["c"] = "Beach";
        Assert.Equal("Desktop 3 (Beach)", sim.World.DescribeDesktop(cells[2]));
    }

    [Fact]
    public void A_follow_mode_dog_comes_to_the_desktop_the_user_switched_to()
    {
        var (sim, cells) = Make();
        var dog = sim.AddPet(PetSpecies.Dog, "Peanut", cells[0], T0);
        dog.Growth = new Growth(T0, Growth.AdultAt);
        dog.Bond.DebugSet(40f, T0);
        Assert.Equal(TravelMode.Follow, dog.Travel);
        Run(sim, 1f);

        sim.ShowCell(cells[1], T0);
        Assert.False(sim.IsVisible(dog));
        Run(sim, 4f);
        Assert.Equal(cells[1].Id, dog.CellId);                    // arrived after the 1-3 s gap
        Assert.IsType<TravelBehavior>(dog.Engine.Current);         // walking in from the left edge
        Assert.True(dog.Position.X < 400f);

        Run(sim, 8f);
        Assert.Null(dog.Trip);
        Assert.True(cells[1].Map.CanRoam(dog.Position));
        Assert.True(sim.IsVisible(dog));
    }

    [Fact]
    public void A_pet_leaving_a_desktop_on_screen_walks_off_the_edge_toward_it()
    {
        var (sim, cells) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Momo", cells[0], T0);
        cat.Growth = new Growth(T0, Growth.AdultAt);
        cat.Position = new Vec2(1500f, 800f);
        TravelPlanner.Go(sim, cat, cells[1], TripReason.Visit, run: false, new Random(1));

        var maxX = 0f;
        for (var i = 0; i < 400 && cat.CellId == cells[0].Id; i++)
        {
            sim.Tick(0.1f, T0);
            if (cat.CellId == cells[0].Id) maxX = System.Math.Max(maxX, cat.Position.X);
        }
        Assert.Equal(cells[1].Id, cat.CellId);
        Assert.True(maxX > 1920f, $"walked out through the right edge (max x {maxX})");

        for (var i = 0; i < 60 && cat.Trip is not null; i++) sim.Tick(0.1f, T0);   // nobody watches desktop 2: it is just there
        Assert.Null(cat.Trip);
        Assert.True(cat.Position.X < 600f, "came in on the left side of the next desktop");
    }

    [Fact]
    public void Home_mode_pets_stay_and_called_pets_come()
    {
        var (sim, cells) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Momo", cells[2], T0);
        cat.Travel = TravelMode.Home;
        cat.HomeCellId = cells[2].Id;
        sim.ShowCell(cells[1], T0);
        Run(sim, 30f);
        Assert.Equal(cells[2].Id, cat.CellId);

        Assert.True(sim.CallPet(cat));
        Run(sim, 8f);
        Assert.Equal(cells[1].Id, cat.CellId);
        Assert.False(sim.CallPet(cat));                            // already here
    }

    [Fact]
    public void A_hungry_pet_goes_to_the_desktop_with_food()
    {
        var (sim, cells) = Make();
        var dog = sim.AddPet(PetSpecies.Dog, "Peanut", cells[2], T0);
        dog.Travel = TravelMode.Free;
        dog.Needs.Hunger = 10f;
        sim.PlaceBowl(cells[0], new Vec2(900f, 900f));
        Assert.Same(cells[0], TravelPlanner.NeedTrip(sim.World, dog, cells[2]));

        Run(sim, 6f);
        Assert.Equal(cells[0].Id, dog.CellId);
    }

    [Fact]
    public void Pets_on_another_desktop_do_not_peek_or_chase_the_pointer()
    {
        var (sim, cells) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Momo", cells[1], T0);
        cat.Travel = TravelMode.Home;
        cat.HomeCellId = cells[1].Id;
        Run(sim, 20f);
        Assert.IsNotType<PeekBehavior>(cat.Engine.Current);
        Assert.IsNotType<HuntCursorBehavior>(cat.Engine.Current);
        Assert.Null(cat.LookAt);
    }

    [Fact]
    public void Travel_mode_is_saved_and_defaults_by_species()
    {
        var (_, cells) = Make();
        var cat = new Pet(PetSpecies.Cat, "Momo", T0, new Random(1)) { CellId = cells[1].Id };
        Assert.Equal(TravelMode.Free, cat.Travel);
        Assert.Equal(TravelMode.Follow, new Pet(PetSpecies.Dog, "Peanut", T0, new Random(1)).Travel);
        cat.Travel = TravelMode.Home;
        cat.HomeCellId = cells[1].Id;

        var save = SaveStore.ToSave(new[] { cat }, T0);
        var back = SaveStore.Restore(save.Pets[0], T0, new Random(2));
        Assert.Equal(TravelMode.Home, back.Travel);
        Assert.Equal(cells[1].Id, back.HomeCellId);
        Assert.Equal(cells[1].Id, back.CellId);

        save.Pets[0].Travel = null;                                // older save
        Assert.Equal(TravelMode.Free, SaveStore.Restore(save.Pets[0], T0, new Random(2)).Travel);
    }
}
