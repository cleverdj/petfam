using PetFam.Core;
using PetFam.Core.Behavior;
using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class PlacesTests
{
    private const int Cols = 64;
    private const int Rows = 40;
    private static readonly RectI Bounds = new(0, 0, 1920, 1200);
    private static readonly RectI Working = new(0, 0, 1920, 1140);

    private static readonly Lab Sky = Lab.FromRgb(120, 170, 230);
    private static readonly Lab Grass = Lab.FromRgb(110, 150, 70);
    private static readonly Lab BrightGrass = Lab.FromRgb(170, 205, 110);
    private static readonly Lab Lake = Lab.FromRgb(40, 90, 150);
    private static readonly Lab Mud = Lab.FromRgb(80, 55, 35);
    private static readonly Lab Trunk = Lab.FromRgb(60, 40, 45);

    /// <summary>
    /// Sky rows 0-11, grass below. A lake (rows 22-29, cols 36-58), a flat mud patch (rows 32-33, cols 5-14),
    /// a sunny patch (rows 35-37, cols 22-28) and a tall tree trunk (cols 2-4, rows 6-25) standing on the grass.
    /// </summary>
    private static Lab[] Scene()
    {
        var cells = new Lab[Cols * Rows];
        for (var r = 0; r < Rows; r++)
        for (var c = 0; c < Cols; c++)
        {
            var color = r < 12 ? Sky : Grass;
            if (r is >= 22 and <= 29 && c is >= 36 and <= 58) color = Lake;
            if (r is >= 32 and <= 33 && c is >= 5 and <= 14) color = Mud;
            if (r is >= 35 and <= 37 && c is >= 22 and <= 28) color = BrightGrass;
            if (c is >= 2 and <= 4 && r is >= 6 and <= 25) color = Trunk;
            cells[r * Cols + c] = color;
        }
        return cells;
    }

    private static WalkMask Mask() => HabitatAnalyzer.Analyze(Scene(), Cols, Rows, Bounds, Working).Mask!;

    [Fact]
    public void Recognises_water_mud_sun_and_shelter()
    {
        var m = Mask();
        Assert.Equal(ZoneType.Water, m.Kind(47, 26));
        Assert.Equal(ZoneType.Mud, m.Kind(9, 32));
        Assert.Equal(ZoneType.Sun, m.Kind(25, 36));
        Assert.Equal(ZoneType.Hide, m.Kind(3, 26));
        Assert.Equal(ZoneType.Ground, m.Kind(30, 30));
        Assert.NotEqual(ZoneType.Water, m.Kind(40, 5));   // the sky is blue but is not water
        Assert.False(m[3, 15]);                           // the trunk itself is not floor
    }

    [Fact]
    public void A_lake_behind_a_sandy_shore_is_water_and_the_shore_is_sand()
    {
        var cells = Scene();
        var sandColor = Lab.FromRgb(225, 205, 150);
        for (var r = 20; r <= 31; r++)
        for (var c = 33; c <= 61; c++)
        {
            var inLake = r is >= 22 and <= 29 && c is >= 36 and <= 58;
            if (!inLake) cells[r * Cols + c] = sandColor;
        }
        var m = HabitatAnalyzer.Analyze(cells, Cols, Rows, Bounds, Working).Mask!;
        Assert.Equal(ZoneType.Water, m.Kind(47, 26));
        Assert.Equal(ZoneType.Sand, m.Kind(47, 30));
        Assert.NotEqual(ZoneType.Hide, m.Kind(47, 32));   // the lake is not a tall object casting shelter
    }

    [Fact]
    public void A_tall_brown_object_is_not_mud()
    {
        var cells = Scene();
        for (var r = 14; r <= 31; r++)
        for (var c = 18; c <= 26; c++)
            cells[r * Cols + c] = Mud;                    // a big brown animal standing on the grass
        var m = HabitatAnalyzer.Analyze(cells, Cols, Rows, Bounds, Working).Mask!;
        Assert.NotEqual(ZoneType.Mud, m.Kind(22, 25));
    }

    private static (Pet pet, BehaviorContext ctx) Make(PetSpecies species, WalkMask mask, int c, int r)
    {
        var map = HabitatMap.FromMask(Bounds, Working, mask);
        var cell = new Cell { Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = Bounds, WorkingArea = Working, Map = map };
        var rng = new Random(4);
        var pet = new Pet(species, "t", DateTime.UtcNow, rng) { CellId = "d:m", Position = mask.CellCenter(c, r) };
        var ctx = new BehaviorContext
        {
            Pet = pet, Cell = cell, User = new UserContext(), Rng = rng, Debug = new DebugOptions(), NowUtc = DateTime.UtcNow,
        };
        return (pet, ctx);
    }

    [Fact]
    public void Dog_swims_in_the_lake_comes_out_wet_and_shakes()
    {
        var mask = Mask();
        var (dog, ctx) = Make(PetSpecies.Dog, mask, 30, 31);
        var swim = new SwimBehavior();
        Assert.True(swim.Score(ctx) > 0f);

        swim.Start(ctx);
        var swam = false;
        var shook = false;
        var done = false;
        for (var i = 0; i < 900 && !done; i++)
        {
            done = swim.Update(ctx, 0.1f);
            swam |= dog.Animation == "swim" && mask.KindAt(dog.Position) == ZoneType.Water;
            shook |= dog.Animation == "shake";
        }
        swim.Stop(ctx, interrupted: false);

        Assert.True(done);
        Assert.True(swam);
        Assert.True(shook);
        Assert.True(mask.IsWalkable(dog.Position));
        Assert.True(dog.Appearance.Wet > 0.3f);
        Assert.Equal(0f, swim.Score(ctx));               // cooling down
    }

    [Fact]
    public void Cats_never_swim()
    {
        var (cat, ctx) = Make(PetSpecies.Cat, Mask(), 30, 31);
        Assert.Equal(0f, new SwimBehavior().Score(ctx));
    }

    [Fact]
    public void Dog_runs_to_the_mud_and_gets_muddy()
    {
        var mask = Mask();
        var (dog, ctx) = Make(PetSpecies.Dog, mask, 30, 36);
        var dig = new DigBehavior();
        dig.Start(ctx);
        for (var i = 0; i < 400 && !dig.Update(ctx, 0.1f); i++) { }
        Assert.Equal(ZoneType.Mud, mask.KindAt(dog.Position));
        Assert.True(dog.Appearance.Mud > 0.4f);
    }

    [Fact]
    public void Cat_lies_down_in_the_sun()
    {
        var mask = Mask();
        var (cat, ctx) = Make(PetSpecies.Cat, mask, 40, 34);
        var lie = new LieBehavior();
        lie.Start(ctx);
        for (var i = 0; i < 200 && cat.Animation != "lie"; i++)
        {
            lie.Update(ctx, 0.1f);
            Simulation.TurnToward(cat, 0.1f);   // the body turns each tick, as in the real simulation
        }
        Assert.Equal("lie", cat.Animation);
        Assert.Equal(ZoneType.Sun, mask.KindAt(cat.Position));
    }

    [Fact]
    public void Dog_sleeps_in_shelter()
    {
        var mask = Mask();
        var (dog, ctx) = Make(PetSpecies.Dog, mask, 20, 30);
        dog.Needs.Energy = 10f;
        var sleep = new SleepBehavior();
        sleep.Start(ctx);
        for (var i = 0; i < 300 && dog.Animation != "sleep"; i++) sleep.Update(ctx, 0.1f);
        Assert.Equal("sleep", dog.Animation);
        Assert.Equal(ZoneType.Hide, mask.KindAt(dog.Position));
    }

    [Fact]
    public void Cat_wandering_avoids_water_and_mud()
    {
        var mask = Mask();
        var (cat, ctx) = Make(PetSpecies.Cat, mask, 30, 31);
        var wander = new WanderBehavior();
        for (var trip = 0; trip < 60; trip++)
        {
            wander.Start(ctx);
            for (var i = 0; i < 400 && !wander.Update(ctx, 0.1f); i++)
                Assert.NotEqual(ZoneType.Water, mask.KindAt(cat.Position));
            Assert.NotEqual(ZoneType.Mud, mask.KindAt(cat.Position));
        }
    }

    private static Lab[] SceneWithHouse()
    {
        var cells = Scene();
        var wall = Lab.FromRgb(205, 175, 140);
        var roof = Lab.FromRgb(170, 60, 50);
        var door = Lab.FromRgb(60, 40, 30);
        for (var r = 18; r <= 30; r++)
        for (var c = 16; c <= 29; c++)
        {
            if (r <= 21) { if (c >= 16 + (21 - r) && c <= 29 - (21 - r)) cells[r * Cols + c] = roof; }
            else if (c is >= 18 and <= 27) cells[r * Cols + c] = r >= 28 && c is 22 or 23 ? door : wall;
        }
        return cells;
    }

    [Fact]
    public void A_house_is_recognised_and_its_door_is_the_entrance()
    {
        var m = HabitatAnalyzer.Analyze(SceneWithHouse(), Cols, Rows, Bounds, Working).Mask!;
        Assert.Equal(ZoneType.House, m.Kind(22, 31));
        Assert.False(m[22, 25]);                         // the walls themselves are not floor
        Assert.NotEqual(ZoneType.Mud, m.Kind(22, 29));   // the dark door is not mud
        Assert.Equal(ZoneType.Mud, m.Kind(9, 32));       // the real mud still is
    }

    [Fact]
    public void A_sleepy_pet_goes_into_the_house_and_comes_out_on_waking()
    {
        var mask = HabitatAnalyzer.Analyze(SceneWithHouse(), Cols, Rows, Bounds, Working).Mask!;
        var (dog, ctx) = Make(PetSpecies.Dog, mask, 40, 34);
        dog.Needs.Energy = 10f;
        var sleep = new SleepBehavior();
        sleep.Start(ctx);
        for (var i = 0; i < 400 && !dog.Hidden; i++) sleep.Update(ctx, 0.1f);
        Assert.True(dog.Hidden);
        Assert.Equal(ZoneType.House, mask.KindAt(dog.Position));
        sleep.Stop(ctx, interrupted: false);
        Assert.False(dog.Hidden);
    }

    [Fact]
    public void A_rectangular_animal_without_clean_edges_is_not_a_house()
    {
        // The plain scene's tree is narrow and blends into the crown above: no house there.
        var m = Mask();
        Assert.Equal(0, m.CountOf(ZoneType.House));
    }

    [Fact]
    public void A_narrow_tall_trunk_is_not_a_house()
    {
        var cells = Scene();
        var bark = Lab.FromRgb(90, 60, 40);
        for (var r = 14; r <= 30; r++)
        for (var c = 30; c <= 33; c++)
            cells[r * Cols + c] = bark;            // 4 wide, 17 tall, clean edges
        var m = HabitatAnalyzer.Analyze(cells, Cols, Rows, Bounds, Working).Mask!;
        Assert.Equal(0, m.CountOf(ZoneType.House));
    }

    [Fact]
    public void A_taller_puddle_still_counts_as_mud_and_keeps_the_grass_behind_it()
    {
        var cells = Scene();
        for (var r = 25; r <= 31; r++)
        for (var c = 15; c <= 32; c++)
            cells[r * Cols + c] = Mud;              // a 7-row-tall puddle in the middle of the grass
        var m = HabitatAnalyzer.Analyze(cells, Cols, Rows, Bounds, Working).Mask!;
        Assert.Equal(ZoneType.Mud, m.Kind(23, 28));
        Assert.True(m[23, 22]);                     // the grass behind the puddle is still floor
    }
}
