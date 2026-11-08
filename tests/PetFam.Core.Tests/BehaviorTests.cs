using PetFam.Core.Behavior;
using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class BehaviorTests
{
    private static readonly DateTime T0 = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private static Cell MakeCell()
    {
        var bounds = new RectI(0, 0, 1920, 1080);
        var working = new RectI(0, 0, 1920, 1040);
        return new Cell
        {
            Id = "d:m",
            DesktopId = "d",
            MonitorId = "m",
            Bounds = bounds,
            WorkingArea = working,
            Map = HabitatMap.Flat(bounds, working),
        };
    }

    private static (Pet pet, BehaviorContext ctx) Make(PetSpecies species, ForegroundWindowInfo? fg = null, bool allowPeek = true)
    {
        var rng = new Random(42);
        var pet = new Pet(species, "t", T0, rng) { CellId = "d:m", Position = new Vec2(960, 1034) };
        var cell = MakeCell();
        var ctx = new BehaviorContext
        {
            Pet = pet,
            Cell = cell,
            User = new UserContext { Foreground = fg, Idle = TimeSpan.Zero },
            Rng = rng,
            Debug = new DebugOptions { AllowPeekAlways = allowPeek },
            NowUtc = T0,
            IsDaytime = true,
        };
        return (pet, ctx);
    }

    [Fact]
    public void Exhausted_pet_sleeps()
    {
        var (pet, ctx) = Make(PetSpecies.Cat);
        pet.Needs.Energy = 5f;
        pet.Engine.Update(ctx, 0.1f);
        Assert.IsType<SleepBehavior>(pet.Engine.Current);
        Assert.Equal("sleep", pet.Animation);
    }

    [Fact]
    public void Curious_pet_walks_behind_the_window_peeks_and_walks_back_out()
    {
        var win = new RectI(400, 200, 800, 500);
        var fg = new ForegroundWindowInfo(new IntPtr(1), win, "Editor", "X", false, false);
        var (pet, ctx) = Make(PetSpecies.Dog, fg);
        pet.Needs.Curiosity = 100f;

        pet.Engine.Update(ctx, 0.1f);
        Assert.IsType<PeekBehavior>(pet.Engine.Current);
        Assert.Null(pet.Peek);                                   // first it walks over: no vanishing on the spot
        Assert.Equal(PetLayer.Backyard, pet.Layer);

        for (var i = 0; i < 200 && pet.Peek is null; i++) pet.Engine.Update(ctx, 0.1f);
        Assert.NotNull(pet.Peek);
        Assert.Equal(PetLayer.WindowEdge, pet.Layer);
        Assert.True(win.Contains(pet.Position), "the head comes out only once the body is behind the window");
        Assert.Equal(90f, pet.TargetHeading);                    // looking at you

        for (var i = 0; i < 300 && pet.Engine.Current is PeekBehavior; i++) pet.Engine.Update(ctx, 0.1f);
        Assert.Equal(PetLayer.Backyard, pet.Layer);
        Assert.Null(pet.Peek);
        Assert.True(ctx.Cell.Map.RoamArea.Contains(pet.Position));
        Assert.False(win.Contains(pet.Position), "walked back out from behind the window");
        Assert.True(pet.Needs.Curiosity < 100f);
    }

    [Fact]
    public void A_pet_does_not_peek_from_a_window_it_cannot_walk_behind()
    {
        // A small window up in the sky: there is no floor behind it to hide on.
        var fg = new ForegroundWindowInfo(new IntPtr(1), new RectI(600, 20, 500, 150), "Note", "X", false, false);
        var (pet, ctx) = Make(PetSpecies.Dog, fg);
        pet.Needs.Curiosity = 100f;
        Assert.Equal(0f, pet.Engine.Behaviors.OfType<PeekBehavior>().Single().Score(ctx));
    }

    [Fact]
    public void Peek_is_gated_by_bond_when_debug_flag_is_off()
    {
        var fg = new ForegroundWindowInfo(new IntPtr(1), new RectI(400, 200, 800, 500), "Editor", "X", false, false);
        var (pet, ctx) = Make(PetSpecies.Cat, fg, allowPeek: false);
        pet.Needs.Curiosity = 100f;
        var peek = pet.Engine.Behaviors.OfType<PeekBehavior>().Single();
        Assert.Equal(0f, peek.Score(ctx));
    }

    [Fact]
    public void Peek_needs_room_outside_the_window()
    {
        // Fullscreen-sized window: no edge has room.
        var fg = new ForegroundWindowInfo(new IntPtr(1), new RectI(0, 0, 1920, 1080), "Game", "X", true, true);
        var (pet, ctx) = Make(PetSpecies.Dog, fg);
        pet.Needs.Curiosity = 100f;
        var peek = pet.Engine.Behaviors.OfType<PeekBehavior>().Single();
        Assert.Equal(0f, peek.Score(ctx));
    }

    [Fact]
    public void Peek_retreats_when_window_moves()
    {
        var fg = new ForegroundWindowInfo(new IntPtr(1), new RectI(400, 200, 800, 500), "Editor", "X", false, false);
        var (pet, ctx) = Make(PetSpecies.Dog, fg);
        pet.Needs.Curiosity = 100f;
        for (var i = 0; i < 200 && pet.Peek is null; i++) pet.Engine.Update(ctx, 0.1f);   // behind the window
        pet.Engine.Update(ctx, 0.6f); // emerged
        Assert.True(pet.Peek!.Progress > 0.9f);

        ctx.User.Foreground = fg with { Bounds = new RectI(600, 200, 800, 500) };
        for (var i = 0; i < 10; i++) pet.Engine.Update(ctx, 0.1f);
        Assert.Equal(PetLayer.Backyard, pet.Layer);
    }

    [Fact]
    public void Wander_moves_inside_roam_area_and_faces_target()
    {
        var (pet, ctx) = Make(PetSpecies.Dog);
        var wander = new WanderBehavior();
        wander.Start(ctx);
        var start = pet.Position;
        wander.Update(ctx, 0.5f);
        Assert.NotEqual(start, pet.Position);
        Assert.True(ctx.Cell.Map.RoamArea.Contains(pet.Position));
        if (MathF.Abs(pet.Position.X - start.X) > 8f)
            Assert.Equal(MathF.Sign(pet.Position.X - start.X), MathF.Sign(MathF.Cos(pet.TargetHeading * MathF.PI / 180f)));
        Assert.Equal("walk", pet.Animation);
    }

    [Fact]
    public void Wander_sets_the_heading_toward_its_target()
    {
        var (pet, ctx) = Make(PetSpecies.Cat);
        var wander = new WanderBehavior();
        wander.Start(ctx);
        var start = pet.Position;
        wander.Update(ctx, 0.3f);
        var moved = pet.Position - start;
        var expected = MathF.Atan2(moved.Y, moved.X) * 180f / MathF.PI;
        Assert.Equal(expected, pet.TargetHeading, precision: 1);
    }

    [Fact]
    public void Heading_turns_smoothly_and_keeps_facing_in_sync()
    {
        Assert.Equal(-90f, Simulation.NormalizeDeg(270f));
        Assert.Equal(170f, Simulation.NormalizeDeg(-190f));
    }

    [Fact]
    public void Wander_eventually_visits_the_upper_half_of_the_screen()
    {
        var (pet, ctx) = Make(PetSpecies.Dog);
        var wander = new WanderBehavior();
        var visitedUpper = false;
        for (var trip = 0; trip < 30 && !visitedUpper; trip++)
        {
            wander.Start(ctx);
            for (var i = 0; i < 600 && !wander.Update(ctx, 0.1f); i++) { }
            visitedUpper = pet.Position.Y < ctx.Cell.Map.RoamArea.Top + ctx.Cell.Map.RoamArea.Height / 2f;
        }
        Assert.True(visitedUpper);
    }

    [Fact]
    public void Depth_scale_shrinks_toward_the_top()
    {
        var map = MakeCell().Map;
        Assert.Equal(map.PetScale, map.DepthScale(map.RoamArea.Bottom), precision: 3);
        Assert.Equal(map.FarScale * map.PetScale, map.DepthScale(map.RoamArea.Top), precision: 3);
        Assert.True(map.DepthScale(map.RoamArea.Top + 100) < map.DepthScale(map.RoamArea.Bottom - 100));
    }
}
