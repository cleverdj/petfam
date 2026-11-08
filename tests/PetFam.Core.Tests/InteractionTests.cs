using PetFam.Core.Behavior;
using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class InteractionTests
{
    private static readonly DateTime T0 = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakePlatform : IPlatform
    {
        public Vec2 Cursor { get; set; }
        public bool OnDesktop { get; set; } = true;
        public void RegisterOwnWindow(IntPtr handle) { }
        public ForegroundWindowInfo? GetForegroundWindow() => null;
        public Vec2 GetCursorPosition() => Cursor;
        public TimeSpan GetIdleTime() => TimeSpan.Zero;
        public bool IsDoNotDisturb() => false;
        public string? GetWallpaperPath() => null;
        public WallpaperFit GetWallpaperFit() => WallpaperFit.Fill;
        public bool IsDesktopAt(Vec2 screenPoint) => OnDesktop;
    }

    private static (Simulation sim, Cell cell, FakePlatform platform) MakeSim()
    {
        var platform = new FakePlatform();
        var sim = new Simulation(platform, new Random(3));
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        var cell = sim.World.Upsert(new Cell
        {
            Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = bounds, WorkingArea = working,
            Map = HabitatMap.Flat(bounds, working),
        });
        return (sim, cell, platform);
    }

    private static void Run(Simulation sim, float seconds, DateTime at)
    {
        var steps = (int)(seconds / 0.1f);
        for (var i = 0; i < steps; i++) sim.Tick(0.1f, at);
    }

    [Fact]
    public void Feeding_places_a_bowl_and_a_hungry_pet_walks_over_and_eats()
    {
        var (sim, cell, _) = MakeSim();
        var pet = sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);
        pet.Needs.Hunger = 30f;

        var bowl = sim.Feed(pet, T0);
        Assert.Single(cell.Bowls);
        Assert.Equal(1f, bowl.Amount);

        Run(sim, 4f, T0);
        Assert.IsType<EatBehavior>(pet.Engine.Current);
        Assert.Equal("eat", pet.Animation);
        Assert.True(Vec2.Distance(pet.Position, bowl.Position) < pet.Species.EatReach() * cell.Map.DepthScale(bowl.Position.Y) + 2f);
        var bondBeforeMeal = pet.Bond.Value;        // includes the daily presence point

        Run(sim, 20f, T0);
        Assert.True(pet.Needs.Hunger > 90f);
        Assert.True(bowl.Amount < 1f);
        Assert.Equal(bondBeforeMeal + 2f, pet.Bond.Value);   // feed credit lands when the meal happens
    }

    [Fact]
    public void A_full_pet_ignores_the_bowl()
    {
        var (sim, cell, _) = MakeSim();
        var pet = sim.AddPet(PetSpecies.Cat, "Momo", cell, T0);
        pet.Needs.Hunger = 100f;
        sim.Feed(pet, T0);
        Run(sim, 5f, T0);
        Assert.IsNotType<EatBehavior>(pet.Engine.Current);
        Assert.Single(cell.Bowls);
    }

    [Fact]
    public void Carrying_preempts_everything_and_drops_inside_the_roam_area()
    {
        var (sim, cell, _) = MakeSim();
        var pet = sim.AddPet(PetSpecies.Cat, "Momo", cell, T0);
        pet.Needs.Energy = 5f;
        Run(sim, 1f, T0);
        Assert.IsType<SleepBehavior>(pet.Engine.Current);

        sim.BeginCarry(pet);
        sim.Tick(0.1f, T0);
        Assert.IsType<HeldBehavior>(pet.Engine.Current);
        Assert.Equal("carried", pet.Animation);

        sim.UpdateCarry(pet, new Vec2(500, 500), new Vec2(0, -80));
        Assert.Equal(new Vec2(500, 580), pet.Position);

        sim.EndCarry(pet, new Vec2(-500, 20000));
        sim.Tick(0.1f, T0);
        Assert.True(cell.Map.RoamArea.Contains(pet.Position));
        Assert.IsNotType<HeldBehavior>(pet.Engine.Current);
    }

    [Fact]
    public void Stroking_is_rate_limited_and_raises_bond()
    {
        var (sim, cell, _) = MakeSim();
        var pet = sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);
        sim.BeginStroke(pet);
        sim.Tick(0.1f, T0);
        Assert.Equal("petted", pet.Animation);
        var before = pet.Bond.Value;             // includes the daily presence point

        Assert.True(sim.StrokeMove(pet, T0));
        Assert.False(sim.StrokeMove(pet, T0));   // too soon
        Run(sim, 0.6f, T0);
        Assert.True(sim.StrokeMove(pet, T0));
        Assert.Equal(before + 2f, pet.Bond.Value);

        sim.EndStroke(pet);
        Assert.Equal(HoldState.None, pet.Hold);
    }

    [Fact]
    public void Pet_looks_at_a_nearby_cursor_only_when_it_is_on_the_desktop()
    {
        var (sim, cell, platform) = MakeSim();
        var pet = sim.AddPet(PetSpecies.Cat, "Momo", cell, T0);
        platform.Cursor = pet.Position + new Vec2(100, -50);

        sim.Tick(0.1f, T0);
        Assert.Equal(platform.Cursor, pet.LookAt);

        platform.OnDesktop = false;
        Run(sim, 0.3f, T0);
        Assert.Null(pet.LookAt);

        platform.OnDesktop = true;
        platform.Cursor = pet.Position + new Vec2(900, 0);
        Run(sim, 0.3f, T0);
        Assert.Null(pet.LookAt);
    }

    [Fact]
    public void A_pat_stops_a_walking_pet_then_it_carries_on()
    {
        var (sim, cell, _) = MakeSim();
        var pet = sim.AddPet(PetSpecies.Cat, "Momo", cell, T0);
        var wander = pet.Engine.Behaviors.OfType<WanderBehavior>().Single();
        pet.Needs.Curiosity = 0f;
        // Get it walking.
        for (var i = 0; i < 200 && pet.Engine.Current is not WanderBehavior; i++) sim.Tick(0.1f, T0);
        Assert.Same(wander, pet.Engine.Current);

        sim.Pat(pet, T0);
        sim.Tick(0.1f, T0);
        Assert.IsType<HeldBehavior>(pet.Engine.Current);
        Assert.Equal("petted", pet.Animation);

        var at = pet.Position;
        Run(sim, 1.2f, T0);
        Assert.Equal(at, pet.Position);              // no sliding while sitting for the pat

        Run(sim, 1.0f, T0);
        Assert.IsNotType<HeldBehavior>(pet.Engine.Current);
    }

    [Fact]
    public void Body_turns_toward_target_heading_over_time()
    {
        var (sim, cell, _) = MakeSim();
        var pet = sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);
        pet.Heading = 0f;
        pet.TargetHeading = 180f;
        Simulation.TurnToward(pet, 0.1f);
        Assert.True(pet.Heading > 0f && pet.Heading < 180f);     // mid-turn
        for (var i = 0; i < 10; i++) Simulation.TurnToward(pet, 0.1f);
        Assert.Equal(180f, MathF.Abs(pet.Heading), precision: 1);
        Assert.Equal(-1f, pet.Facing);
    }

    [Fact]
    public void Waking_up_is_followed_by_a_stretch()
    {
        var (sim, cell, _) = MakeSim();
        var pet = sim.AddPet(PetSpecies.Dog, "Peanut", cell, T0);
        pet.Needs.Energy = 95f;    // sleeps only briefly, wakes at 100
        pet.Engine.Update(new BehaviorContext
        {
            Pet = pet, Cell = cell, User = sim.User, Rng = new Random(1), Debug = sim.Debug, NowUtc = T0,
        }, 0.1f);

        // Force a nap, then let it finish.
        var sleep = pet.Engine.Behaviors.OfType<SleepBehavior>().Single();
        pet.Needs.Energy = 10f;
        Run(sim, 4f, T0);
        Assert.Same(sleep, pet.Engine.Current);
        pet.Needs.Energy = 100f;
        Run(sim, 0.5f, T0);
        Assert.IsType<StretchBehavior>(pet.Engine.Current);
        Assert.Equal("stretch", pet.Animation);
    }
}
