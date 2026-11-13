using PetFam.Core.Geometry;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Core.Tests;

public class OfficialHabitatTests
{
    private static string Repo([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    private static string[] Rows(string id) =>
        File.ReadAllLines(Path.Combine(Repo(), "src", "PetFam.App", "Assets", "Wallpapers", id + ".habitat.txt"));

    [Fact]
    public void Every_official_wallpaper_has_a_picture_and_a_full_map_with_floor_to_walk_on()
    {
        foreach (var w in OfficialHabitat.All)
        {
            Assert.True(File.Exists(Path.Combine(Repo(), "src", "PetFam.App", "Assets", "Wallpapers", w.Id + ".jpg")), w.Id);
            var rows = Rows(w.Id);
            Assert.Equal(72, rows.Length);
            Assert.All(rows, r => Assert.Equal(128, r.Length));
            var mask = OfficialHabitat.MaskFor(rows, w.Width, w.Height, new RectI(0, 0, 1920, 1080), WallpaperFit.Fill);
            var map = HabitatMap.FromMask(new RectI(0, 0, 1920, 1080), new RectI(0, 0, 1920, 1040), mask);
            Assert.True(mask.Count > 1000, $"{w.Id}: {mask.Count} floor spots");
            Assert.True(map.RoamArea.Height > 200, $"{w.Id}: roam {map.RoamArea}");
        }
        Assert.Equal("sunny-meadow", OfficialHabitat.FromPath(@"C:\Users\x\AppData\Local\PetFam\Wallpapers\petfam-sunny-meadow.jpg")?.Id);
        Assert.Null(OfficialHabitat.FromPath(@"C:\pics\meadow.jpg"));
    }

    [Fact]
    public void The_meadow_pond_is_water_where_the_picture_shows_it_on_any_screen_shape()
    {
        var rows = Rows("sunny-meadow");
        // The pond is drawn centred at (1840, 1010) in the 2560x1440 picture.
        foreach (var screen in new[] { new RectI(0, 0, 2560, 1440), new RectI(2560, 0, 1920, 1080), new RectI(0, 0, 2560, 1600) })
        {
            var mask = OfficialHabitat.MaskFor(rows, 2560, 1440, screen, WallpaperFit.Fill);
            // Fill: scale to cover, centred.
            var k = Math.Max(screen.Width / 2560.0, screen.Height / 1440.0);
            var ox = (screen.Width - 2560 * k) / 2;
            var oy = (screen.Height - 1440 * k) / 2;
            var pond = new Vec2(screen.X + (float)(ox + 1840 * k), screen.Y + (float)(oy + 1010 * k));
            Assert.Equal(ZoneType.Water, mask.KindAt(pond));
            var sky = new Vec2(screen.X + screen.Width / 2f, screen.Y + 60);
            Assert.Equal(ZoneType.Sky, mask.KindAt(sky));
        }

        // Fit leaves bars beside the picture: not floor.
        var fit = OfficialHabitat.MaskFor(rows, 2560, 1440, new RectI(0, 0, 1000, 1000), WallpaperFit.Fit);
        Assert.Equal(ZoneType.Blocked, fit.KindAt(new Vec2(500, 20)));
    }
}
