using PetFam.Core.Geometry;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class HabitatEditsTests
{
    private static readonly RectI Screen = new(1920, 0, 1920, 1080);

    private static WalkMask Auto()
    {
        // Top half sky/blocked, bottom half ground, with a pond in the middle of the floor.
        const int cols = 64, rows = 36;
        var kinds = new ZoneType[cols * rows];
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
            kinds[r * cols + c] = r < rows / 2 ? ZoneType.Blocked : c is >= 28 and < 36 && r is >= 24 and < 30 ? ZoneType.Water : ZoneType.Ground;
        return new WalkMask(cols, rows, Screen, kinds);
    }

    [Fact]
    public void A_stroke_paints_its_round_patch_and_nothing_else()
    {
        var auto = Auto();
        // Floor up on the "hill" at the top left (relative coordinates).
        var mask = HabitatEdits.Apply(auto, new[] { new HabitatStroke(0.2f, 0.25f, 0.05f, ZoneType.Ground) });
        Assert.True(mask.IsWalkable(new Vec2(Screen.X + 0.2f * Screen.Width, Screen.Y + 0.25f * Screen.Height)));
        Assert.False(mask.IsWalkable(new Vec2(Screen.X + 0.6f * Screen.Width, Screen.Y + 0.25f * Screen.Height)));
        Assert.True(mask.Count > auto.Count);
        Assert.True(mask.Count - auto.Count < 60);
    }

    [Fact]
    public void Later_strokes_win_and_the_eraser_brings_back_the_analysis()
    {
        var auto = Auto();
        var pond = new Vec2(Screen.X + 0.5f * Screen.Width, Screen.Y + 0.75f * Screen.Height);
        Assert.Equal(ZoneType.Water, auto.KindAt(pond));

        var strokes = new List<HabitatStroke>
        {
            new(0.5f, 0.75f, 0.06f, ZoneType.Ground),   // fill the pond in
            new(0.5f, 0.75f, 0.03f, ZoneType.Mud),      // a muddy patch in the middle of it
        };
        var mask = HabitatEdits.Apply(auto, strokes);
        Assert.Equal(ZoneType.Mud, mask.KindAt(pond));
        Assert.Equal(ZoneType.Ground, mask.KindAt(pond + new Vec2(0.045f * Screen.Width, 0)));

        strokes.Add(new HabitatStroke(0.5f, 0.75f, 0.08f, null));
        Assert.Equal(ZoneType.Water, HabitatEdits.Apply(auto, strokes).KindAt(pond));
    }

    [Fact]
    public void Edits_are_kept_per_wallpaper_and_screen_size_and_survive_a_round_trip()
    {
        var edits = new HabitatEdits();
        var key = HabitatEdits.KeyFor(@"C:\pics\lake.jpg|123|456|Fill", Screen);
        Assert.NotEqual(key, HabitatEdits.KeyFor(@"C:\pics\lake.jpg|123|456|Fill", new RectI(0, 0, 2560, 1600)));
        edits.Set(key, new[] { new HabitatStroke(0.1f, 0.9f, 0.04f, ZoneType.Sand), new HabitatStroke(0.3f, 0.8f, 0.02f, null) });

        var path = Path.Combine(Path.GetTempPath(), $"petfam-edits-{Guid.NewGuid():N}.json");
        try
        {
            edits.Write(path);
            var back = HabitatEdits.Read(path);
            Assert.Equal(edits.For(key), back.For(key));
            Assert.Empty(back.For("another wallpaper"));
        }
        finally { File.Delete(path); }

        edits.Set(key, Array.Empty<HabitatStroke>());
        Assert.Empty(edits.ByKey);
    }

    [Fact]
    public void A_screen_without_analysis_gets_a_flat_floor_to_paint_on()
    {
        var work = new RectI(Screen.X, Screen.Y, Screen.Width, Screen.Height - 40);
        var flat = HabitatMap.Flat(Screen, work);
        var mask = HabitatEdits.FlatMask(flat);
        Assert.Equal(128, mask.Cols);
        Assert.True(mask.IsWalkable(new Vec2(Screen.X + 960, 900)));
        Assert.Equal(ZoneType.Sky, mask.KindAt(new Vec2(Screen.X + 960, 50)));
        var map = HabitatMap.FromMask(Screen, work, mask);
        Assert.True(map.CanRoam(new Vec2(Screen.X + 960, 900)));
    }
}
