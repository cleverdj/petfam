using PetFam.Core.Behavior;
using PetFam.Core.Behavior.Behaviors;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class HabitatAnalyzerTests
{
    private const int Cols = 64;
    private const int Rows = 40;
    private static readonly RectI Bounds = new(0, 0, 1920, 1200);
    private static readonly RectI Working = new(0, 0, 1920, 1140);

    private static readonly Lab Sky = Lab.FromRgb(120, 170, 230);
    private static readonly Lab Grass = Lab.FromRgb(200, 170, 90);
    private static readonly Lab Elk = Lab.FromRgb(70, 45, 30);

    /// <summary>Sky above row 16, grass below, a dark animal standing on the grass (cols 10-25, rows 14-34).</summary>
    private static Lab[] Scene(bool withAnimal = true, int horizon = 16)
    {
        var cells = new Lab[Cols * Rows];
        var rng = new Random(1);
        for (var r = 0; r < Rows; r++)
        for (var c = 0; c < Cols; c++)
        {
            var baseColor = r < horizon ? Sky : Grass;
            if (withAnimal && c is >= 10 and <= 25 && r is >= 14 and <= 34) baseColor = Elk;
            // a little texture
            var j = (float)(rng.NextDouble() - 0.5) * 4f;
            cells[r * Cols + c] = new Lab(baseColor.L + j, baseColor.A + j * 0.3f, baseColor.B + j * 0.3f);
        }
        return cells;
    }

    [Fact]
    public void Grass_is_floor_sky_and_animal_are_not()
    {
        var result = HabitatAnalyzer.Analyze(Scene(), Cols, Rows, Bounds, Working);
        var m = result.Mask;
        Assert.NotNull(m);

        Assert.True(m![40, 30]);          // open grass
        Assert.True(m[5, 38 - 1]);        // grass near the bottom-left
        Assert.False(m[40, 5]);           // sky
        Assert.False(m[17, 20]);          // the animal's body
        Assert.False(m[17, 30]);
    }

    [Fact]
    public void Floor_coloured_patch_on_top_of_an_object_is_not_floor()
    {
        var cells = Scene();
        // Light, grass-coloured fur on the animal's back, connected to the grass on the left.
        for (var r = 15; r <= 19; r++)
        for (var c = 6; c <= 18; c++)
            cells[r * Cols + c] = Grass;

        var m = HabitatAnalyzer.Analyze(cells, Cols, Rows, Bounds, Working).Mask!;
        Assert.False(m[14, 17]);   // on the back, above the dark body
        Assert.True(m[40, 30]);    // open grass still floor
    }

    [Fact]
    public void Horizon_becomes_the_top_of_the_roam_area()
    {
        var mask = HabitatAnalyzer.Analyze(Scene(withAnimal: false, horizon: 20), Cols, Rows, Bounds, Working).Mask!;
        var map = HabitatMap.FromMask(Bounds, Working, mask);
        var cellH = Bounds.Height / (float)Rows;
        Assert.InRange(map.RoamArea.Top, 20 * cellH - cellH, 20 * cellH + cellH);
        Assert.Equal(map.FarScale * map.PetScale, map.DepthScale(map.RoamArea.Top), precision: 3);
    }

    [Fact]
    public void Clamp_moves_points_off_the_animal_onto_grass()
    {
        var mask = HabitatAnalyzer.Analyze(Scene(), Cols, Rows, Bounds, Working).Mask!;
        var map = HabitatMap.FromMask(Bounds, Working, mask);
        var onAnimal = mask.CellCenter(17, 28);
        Assert.False(map.CanRoam(onAnimal));
        var clamped = map.ClampToRoam(onAnimal);
        Assert.True(mask.IsWalkable(clamped));
    }

    [Fact]
    public void Wandering_only_targets_floor_reachable_in_a_straight_line()
    {
        var mask = HabitatAnalyzer.Analyze(Scene(), Cols, Rows, Bounds, Working).Mask!;
        var map = HabitatMap.FromMask(Bounds, Working, mask);
        var cell = new Cell { Id = "d:m", DesktopId = "d", MonitorId = "m", Bounds = Bounds, WorkingArea = Working, Map = map };
        var rng = new Random(9);
        var pet = new Pet(PetSpecies.Dog, "t", DateTime.UtcNow, rng) { CellId = "d:m", Position = mask.CellCenter(45, 30) };
        var ctx = new BehaviorContext
        {
            Pet = pet, Cell = cell, User = new UserContext(), Rng = rng, Debug = new DebugOptions(), NowUtc = DateTime.UtcNow,
        };

        var wander = new WanderBehavior();
        for (var trip = 0; trip < 40; trip++)
        {
            wander.Start(ctx);
            for (var i = 0; i < 400 && !wander.Update(ctx, 0.1f); i++)
                Assert.True(mask.IsWalkable(pet.Position), $"left the floor at {pet.Position}");
        }
    }

    [Fact]
    public void A_screen_without_a_recognisable_floor_falls_back()
    {
        // Busy noise everywhere: the bottom band has no consistent colour, so nothing grows.
        var rng = new Random(3);
        var cells = Enumerable.Range(0, Cols * Rows)
            .Select(_ => Lab.FromRgb((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256))).ToArray();
        var result = HabitatAnalyzer.Analyze(cells, Cols, Rows, Bounds, Working);
        Assert.Null(result.Mask);
    }

    /// <summary>
    /// A photo of a lake, with roughness per cell: smooth sky rows 0-7, rough wooded hills rows 8-19, the waterline at
    /// row 20. On the left (cols 0-39) the lake mirrors the hills down to the bottom; on the right a rough pebble shore
    /// of the same bluish colour as the water (a colour-graded photo).
    /// </summary>
    private static (Lab[] Cells, float[] Texture) LakePhoto()
    {
        var cells = new Lab[Cols * Rows];
        var tex = new float[Cols * Rows];
        Lab Hill(int c, int r) => new(30 + 25 * MathF.Sin(c * 0.45f) * (r - 7) / 12f + (r - 8) * 1.5f, 2, -6);
        for (var r = 0; r < Rows; r++)
        for (var c = 0; c < Cols; c++)
        {
            var i = r * Cols + c;
            if (r < 8) { cells[i] = new Lab(72, -2, -18); tex[i] = 0.5f; }
            else if (r < 20) { cells[i] = Hill(c, r); tex[i] = 4.5f; }
            else if (c < 40)
            {
                var m = 39 - r;   // mirror row about the waterline (between rows 19 and 20)
                var h = m >= 8 ? Hill(c, m) : new Lab(66, -2, -14);
                cells[i] = new Lab(h.L - 4, h.A, h.B - 3);
                tex[i] = 1.0f;
            }
            else { cells[i] = new Lab(40, 6, -11); tex[i] = 6.0f; }
        }
        return (cells, tex);
    }

    [Fact]
    public void Lake_is_water_and_the_rough_shore_of_the_same_colour_is_floor()
    {
        var (cells, tex) = LakePhoto();
        var result = HabitatAnalyzer.Analyze(cells, Cols, Rows, Bounds, Working, tex);
        var m = result.Mask;
        Assert.NotNull(m);
        Assert.Contains("mirror line", result.Report);

        Assert.Equal(ZoneType.Water, m!.Kind(15, 30));     // the lake
        Assert.Equal(ZoneType.Water, m.Kind(15, 22));      // reflected hills just under the waterline
        Assert.True(m[52, 33]);                             // pebbles: same colour as the water, but rough
        Assert.False(m[30, 4]);                             // sky
        Assert.False(m[20, 14]);                            // hills
    }

    [Fact]
    public void A_plain_wallpaper_is_floor_everywhere()
    {
        var cells = Enumerable.Repeat(new Lab(97, 0, 0), Cols * Rows).ToArray();
        var tex = new float[Cols * Rows];
        var m = HabitatAnalyzer.Analyze(cells, Cols, Rows, Bounds, Working, tex).Mask;
        Assert.NotNull(m);
        Assert.True(m![30, 3]);
        Assert.True(m[30, 30]);
    }
}
