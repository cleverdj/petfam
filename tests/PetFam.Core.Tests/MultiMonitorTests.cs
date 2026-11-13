using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class MultiMonitorTests
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

    /// <summary>
    /// Two virtual desktops, each on two monitors: a 2560x1600 main monitor and a 1920x1080 one to its right, set a
    /// little lower. The user looks at desktop "a".
    /// </summary>
    private static (Simulation Sim, Cell A1, Cell A2, Cell B1, Cell B2) Make()
    {
        var sim = new Simulation(new FakePlatform(), new Random(7));
        var screens = new (string Id, RectI Bounds)[] { ("m1", new RectI(0, 0, 2560, 1600)), ("m2", new RectI(2560, 200, 1920, 1080)) };
        var cells = new Dictionary<string, Cell>();
        foreach (var desk in new[] { "a", "b" })
        foreach (var (id, b) in screens)
        {
            var work = new RectI(b.X, b.Y, b.Width, b.Height - 48);
            cells[desk + id] = sim.World.Upsert(new Cell
            {
                Id = Cell.MakeId(desk, id), DesktopId = desk, MonitorId = id, Bounds = b, WorkingArea = work, Map = HabitatMap.Flat(b, work),
            });
        }
        sim.World.DesktopOrder.AddRange(new[] { "a", "b" });
        sim.World.MonitorOrder.AddRange(new[] { "m1", "m2" });
        sim.ShowCells(new[] { cells["am1"], cells["am2"] }, T0);
        return (sim, cells["am1"], cells["am2"], cells["bm1"], cells["bm2"]);
    }

    private static void Run(Simulation sim, float seconds, Action? each = null)
    {
        for (var t = 0f; t < seconds; t += 0.1f)
        {
            sim.Tick(0.1f, T0);
            each?.Invoke();
        }
    }

    [Fact]
    public void Every_monitor_of_the_current_desktop_is_on_screen()
    {
        var (sim, a1, a2, b1, b2) = Make();
        var cat = sim.AddPet(PetSpecies.Cat, "Momo", a2, T0);
        Assert.True(sim.IsVisible(cat));
        Assert.Equal(a1.Id, sim.VisibleCellId);
        Assert.Contains(a2.Id, sim.VisibleCellIds);

        Assert.Same(a2, sim.World.MonitorNeighbor(a1, +1));
        Assert.Same(a1, sim.World.MonitorNeighbor(a2, -1));
        Assert.Null(sim.World.MonitorNeighbor(a1, -1));
        Assert.Same(b2, sim.World.Neighbor(a2, +1));          // the next desktop on the same monitor
        Assert.Same(b1, sim.World.CellForDesktop("b"));        // a desktop's main cell is on the main monitor
        Assert.Equal("Desktop 2 · monitor 2", sim.World.DescribeCell(b2));
    }

    [Fact]
    public void A_pet_walks_off_one_screen_and_in_from_the_facing_edge_of_the_next()
    {
        var (sim, a1, a2, _, _) = Make();
        var dog = sim.AddPet(PetSpecies.Dog, "Peanut", a1, T0);
        dog.Growth = new Growth(T0, Growth.AdultAt);
        dog.Position = new Vec2(1800, 1300);
        Run(sim, 1f);

        TravelPlanner.Go(sim, dog, a2, TripReason.Visit, run: false, new Random(1));
        var leftViaRight = false;
        var cameInFromLeft = false;
        var alwaysVisible = true;
        Run(sim, 30f, () =>
        {
            if (dog.CellId == a1.Id && dog.Position.X > a1.Bounds.Right) leftViaRight = true;
            if (dog.CellId == a2.Id && dog.Trip is not null && dog.Position.X < a2.Bounds.X) cameInFromLeft = true;
            alwaysVisible &= sim.IsVisible(dog);
        });
        Assert.True(leftViaRight, "walked off the right edge of the main monitor");
        Assert.True(cameInFromLeft, "came in from the left edge of the second monitor");
        Assert.True(alwaysVisible);
        Assert.Equal(a2.Id, dog.CellId);
        Assert.Null(dog.Trip);
        Assert.True(a2.Map.CanRoam(dog.Position));
    }

    [Fact]
    public void Pets_wander_between_screens_now_and_then_but_a_stay_home_pet_does_not()
    {
        var (sim, a1, a2, _, _) = Make();
        var free = sim.AddPet(PetSpecies.Cat, "Momo", a1, T0);
        free.Travel = TravelMode.Free;
        var home = sim.AddPet(PetSpecies.Dog, "Bori", a1, T0);
        home.Travel = TravelMode.Home;
        home.HomeCellId = a1.Id;
        foreach (var p in new[] { free, home }) p.Growth = new Growth(T0, Growth.AdultAt);

        var crossings = 0;
        var was = free.CellId;
        Run(sim, 2 * 3600f, () =>
        {
            if (free.CellId != was) { crossings++; was = free.CellId; }
            Assert.Equal(a1.Id, home.CellId);
        });
        Assert.True(crossings >= 2, $"crossed {crossings} times in two hours");
        Assert.True(crossings < 60, $"crossed {crossings} times: too restless");
        Assert.Contains(free.CellId, new[] { a1.Id, a2.Id });     // never left the desktop on screen by itself
    }

    [Fact]
    public void After_a_desktop_switch_a_pet_follows_on_its_own_monitor()
    {
        var (sim, _, a2, b1, b2) = Make();
        var dog = sim.AddPet(PetSpecies.Dog, "Peanut", a2, T0);
        dog.Growth = new Growth(T0, Growth.AdultAt);
        dog.Bond.DebugSet(40f, T0);
        Run(sim, 1f);

        sim.ShowCells(new[] { b1, b2 }, T0);
        Run(sim, 12f);
        Assert.Equal(b2.Id, dog.CellId);
        Assert.True(sim.IsVisible(dog));
    }
}
