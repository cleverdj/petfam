using PetFam.Core.Behavior;
using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class StuckTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

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
    /// The floor with a patch that isn't floor (a tree, a rock in the wallpaper) between the pet and the right edge,
    /// which it can get round above or below.
    /// </summary>
    private static WalkMask FloorWithRock()
    {
        const int cols = 64, rows = 36;
        var kinds = new ZoneType[cols * rows];
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
            kinds[r * cols + c] = r < 12 || (c is >= 44 and < 50 && r is >= 20 and < 26) ? ZoneType.Blocked : ZoneType.Ground;
        return new WalkMask(cols, rows, new RectI(0, 0, 1920, 1080), kinds);
    }

    [Fact]
    public void A_pet_off_to_the_next_screen_gets_past_a_patch_that_is_not_floor()
    {
        var sim = new Simulation(new FakePlatform(), new Random(3));
        var w = new RectI(0, 0, 1920, 1040);
        var here = sim.World.Upsert(new Cell { Id = "d:a", DesktopId = "d", MonitorId = "a", Bounds = new RectI(0, 0, 1920, 1080), WorkingArea = w,
            Map = HabitatMap.FromMask(new RectI(0, 0, 1920, 1080), w, FloorWithRock()) });
        var there = sim.World.Upsert(new Cell { Id = "d:b", DesktopId = "d", MonitorId = "b", Bounds = new RectI(1920, 0, 1920, 1080),
            WorkingArea = new RectI(1920, 0, 1920, 1040), Map = HabitatMap.Flat(new RectI(1920, 0, 1920, 1080), new RectI(1920, 0, 1920, 1040)) });
        var pet = sim.AddPet(Breed.Shiba, "Happy", here, T0);
        pet.Growth = new Growth(T0, Growth.AdultAt);
        pet.Position = new Vec2(1200, 23 * 30f);                       // level with the rock, left of it
        pet.Trip = new Trip { To = there, Reason = TripReason.Visit, Direction = 1 };

        var travel = new TravelBehavior();
        var ctx = new BehaviorContext { Pet = pet, Cell = here, User = sim.User, Rng = new Random(1), Debug = sim.Debug, NowUtc = T0, Pets = sim.Pets, Social = sim.Social, Visible = true };
        travel.Start(ctx);
        for (var t = 0f; t < 30f && pet.CellId != there.Id; t += 0.1f)
            travel.Update(ctx, 0.1f);
        Assert.Equal(there.Id, pet.CellId);
    }
}

public class HoverTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

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
    public void A_pet_under_the_mouse_stops_wandering_off_and_stays_to_be_picked_up()
    {
        var sim = new Simulation(new FakePlatform(), new Random(3));
        var b = new RectI(0, 0, 1920, 1080);
        var cell = sim.World.Upsert(new Cell { Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = b, WorkingArea = new RectI(0, 0, 1920, 1040), Map = HabitatMap.Flat(b, new RectI(0, 0, 1920, 1040)) });
        var pet = sim.AddPet(Breed.Shiba, "Happy", cell, T0);
        pet.Growth = new Growth(T0, Growth.AdultAt);
        pet.Needs.Energy = 100f;
        pet.Personality.Activity = 1f;

        // Get it walking somewhere.
        for (var t = 0f; t < 120f && pet.Engine.Current is not WanderBehavior; t += 0.25f) sim.Tick(0.25f, T0.AddSeconds(t));
        Assert.IsType<WanderBehavior>(pet.Engine.Current);

        pet.Hovered = true;
        Assert.False(PetFam.Core.Social.SocialDirector.Available(pet));
        Assert.False(TravelPlanner.CanLeave(pet));
        var at = pet.Position;
        for (var t = 0f; t < 20f; t += 0.25f) sim.Tick(0.25f, T0.AddSeconds(200 + t));
        Assert.IsNotType<WanderBehavior>(pet.Engine.Current);
        Assert.True(Vec2.Distance(at, pet.Position) < 40f, $"moved {Vec2.Distance(at, pet.Position):0} px while under the mouse");
    }
}
