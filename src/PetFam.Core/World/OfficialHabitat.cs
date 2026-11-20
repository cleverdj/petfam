using PetFam.Core.Geometry;
using PetFam.Core.Platform;

namespace PetFam.Core.World;

/// <summary>
/// PetFam's own wallpapers: pictures drawn together with an exact habitat map (build/wallpapers/make_wallpapers.py),
/// so pets know every pond, sandy path and patch of shade without guessing. The map is a grid of letters over the
/// picture; it is laid onto a screen the way Windows lays the picture (fill, fit, stretch, centre, tile).
/// </summary>
public static class OfficialHabitat
{
    public sealed record Wallpaper(string Id, Loc Name, int Width, int Height);

    public static readonly IReadOnlyList<Wallpaper> All = new[]
    {
        new Wallpaper("sunny-meadow", new Loc("Sunny meadow", "햇살 초원"), 2560, 1440),
        new Wallpaper("seaside", new Loc("Seaside", "바닷가"), 2560, 1440),
        new Wallpaper("cozy-garden", new Loc("Cozy garden", "아늑한 정원"), 2560, 1440),
    };

    /// <summary>File name PetFam gives an official wallpaper when it puts it on the desktop.</summary>
    public static string FileName(Wallpaper w) => $"petfam-{w.Id}.jpg";

    /// <summary>The official wallpaper this file is (by its file name), or null.</summary>
    public static Wallpaper? FromPath(string? path)
    {
        if (path is null) return null;
        // Either kind of slash: a Windows path can turn up in a save file opened on a Mac.
        var name = path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
        return All.FirstOrDefault(w => string.Equals(name, FileName(w), StringComparison.OrdinalIgnoreCase));
    }

    public static ZoneType KindOf(char letter) => letter switch
    {
        'g' => ZoneType.Ground,
        'w' => ZoneType.Water,
        'm' => ZoneType.Mud,
        's' => ZoneType.Sand,
        'u' => ZoneType.Sun,
        'h' => ZoneType.Hide,
        'k' => ZoneType.Sky,
        _ => ZoneType.Blocked,
    };

    /// <summary>
    /// The habitat of a picture of imageW x imageH whose map is <paramref name="rows"/> (one letter per spot), as it
    /// lies on a screen of these bounds with this fit.
    /// </summary>
    public static WalkMask MaskFor(IReadOnlyList<string> rows, int imageW, int imageH, RectI bounds, WallpaperFit fit, int cols = 128)
    {
        var gridRows = rows.Count;
        var gridCols = rows.Count == 0 ? 0 : rows[0].Length;
        var screenRows = Math.Max(20, (int)Math.Round(cols * bounds.Height / (double)Math.Max(1, bounds.Width)));
        var kinds = new ZoneType[cols * screenRows];
        double sw = bounds.Width, sh = bounds.Height;

        // Screen -> picture: scale and offset of the picture on the screen.
        double kx, ky, ox, oy;
        switch (fit)
        {
            case WallpaperFit.Stretch:
                kx = sw / imageW; ky = sh / imageH; ox = 0; oy = 0;
                break;
            case WallpaperFit.Fit:
                kx = ky = Math.Min(sw / imageW, sh / imageH);
                ox = (sw - imageW * kx) / 2; oy = (sh - imageH * ky) / 2;
                break;
            case WallpaperFit.Center:
            case WallpaperFit.Tile:
                kx = ky = 1; ox = (sw - imageW) / 2; oy = (sh - imageH) / 2;
                break;
            default:   // Fill, Span
                kx = ky = Math.Max(sw / imageW, sh / imageH);
                ox = (sw - imageW * kx) / 2; oy = (sh - imageH * ky) / 2;
                break;
        }

        for (var r = 0; r < screenRows; r++)
        for (var c = 0; c < cols; c++)
        {
            var x = (c + 0.5) * sw / cols;
            var y = (r + 0.5) * sh / screenRows;
            var ix = (x - ox) / kx;
            var iy = (y - oy) / ky;
            if (fit == WallpaperFit.Tile)
            {
                ix = ((ix % imageW) + imageW) % imageW;
                iy = ((iy % imageH) + imageH) % imageH;
            }
            var kind = ZoneType.Blocked;
            if (ix >= 0 && iy >= 0 && ix < imageW && iy < imageH && gridRows > 0)
            {
                var gc = Math.Min(gridCols - 1, (int)(ix / imageW * gridCols));
                var gr = Math.Min(gridRows - 1, (int)(iy / imageH * gridRows));
                kind = gc < rows[gr].Length ? KindOf(rows[gr][gc]) : ZoneType.Blocked;
            }
            kinds[r * cols + c] = kind;
        }
        return new WalkMask(cols, screenRows, bounds, kinds);
    }
}
