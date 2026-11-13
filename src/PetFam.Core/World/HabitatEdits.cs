using System.Text.Json;
using System.Text.Json.Serialization;
using PetFam.Core.Geometry;

namespace PetFam.Core.World;

/// <summary>
/// One brush stroke of the habitat editor: a round dab at (X, Y) with radius R, all relative to the monitor
/// (0..1 of its width and height; R is a share of the width), painting <see cref="Kind"/> there.
/// A null kind is the eraser: back to what the wallpaper analysis found.
/// </summary>
public sealed record HabitatStroke(float X, float Y, float R, ZoneType? Kind);

/// <summary>
/// Hand corrections to the wallpaper analysis (the habitat editor): for each wallpaper and screen size, the strokes
/// the user painted. They are laid over the automatic result every time it is made, so they survive restarts and
/// re-analysis, and a different wallpaper starts clean.
/// </summary>
public sealed class HabitatEdits
{
    /// <summary>Kinds the editor paints: floor, water, mud, sand, a sunny spot, shade / shelter, and not-floor.</summary>
    public static readonly ZoneType[] Paintable =
        { ZoneType.Ground, ZoneType.Water, ZoneType.Mud, ZoneType.Sand, ZoneType.Sun, ZoneType.Hide, ZoneType.Blocked };

    public Dictionary<string, List<HabitatStroke>> ByKey { get; init; } = new();

    /// <summary>Edits belong to a wallpaper (its analysis key: file, size, date, fit) on a screen of a given size.</summary>
    public static string KeyFor(string wallpaperKey, RectI bounds) => $"{wallpaperKey}|{bounds.Width}x{bounds.Height}";

    public IReadOnlyList<HabitatStroke> For(string key) => ByKey.TryGetValue(key, out var s) ? s : Array.Empty<HabitatStroke>();

    public void Set(string key, IReadOnlyList<HabitatStroke> strokes)
    {
        if (strokes.Count == 0) ByKey.Remove(key);
        else ByKey[key] = strokes.ToList();
    }

    /// <summary>The automatic mask with the strokes painted over it, in order (later strokes win).</summary>
    public static WalkMask Apply(WalkMask auto, IReadOnlyList<HabitatStroke> strokes)
    {
        var kinds = new ZoneType[auto.Cols * auto.Rows];
        for (var r = 0; r < auto.Rows; r++)
        for (var c = 0; c < auto.Cols; c++)
            kinds[r * auto.Cols + c] = auto.Kind(c, r);
        if (strokes.Count == 0) return new WalkMask(auto.Cols, auto.Rows, auto.Bounds, kinds);

        var w = (float)auto.Bounds.Width;
        var h = (float)auto.Bounds.Height;
        foreach (var s in strokes)
        {
            var rpx = s.R * w;
            int c0 = Math.Max(0, (int)((s.X * w - rpx) / auto.CellW)), c1 = Math.Min(auto.Cols - 1, (int)((s.X * w + rpx) / auto.CellW));
            int r0 = Math.Max(0, (int)((s.Y * h - rpx) / auto.CellH)), r1 = Math.Min(auto.Rows - 1, (int)((s.Y * h + rpx) / auto.CellH));
            for (var r = r0; r <= r1; r++)
            for (var c = c0; c <= c1; c++)
            {
                var dx = (c + 0.5f) * auto.CellW - s.X * w;
                var dy = (r + 0.5f) * auto.CellH - s.Y * h;
                if (dx * dx + dy * dy > rpx * rpx) continue;
                kinds[r * auto.Cols + c] = s.Kind ?? auto.Kind(c, r);
            }
        }
        return new WalkMask(auto.Cols, auto.Rows, auto.Bounds, kinds);
    }

    /// <summary>
    /// A mask for a screen with no analysis (plain colour, no wallpaper file): floor where the flat map lets pets
    /// roam, sky above it, nothing elsewhere. The editor paints over this.
    /// </summary>
    public static WalkMask FlatMask(HabitatMap flat, int cols = 128)
    {
        var b = flat.Bounds;
        var rows = Math.Max(20, (int)Math.Round(cols * b.Height / (double)Math.Max(1, b.Width)));
        var kinds = new ZoneType[cols * rows];
        var cw = b.Width / (float)cols;
        var ch = b.Height / (float)rows;
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
        {
            var p = new Vec2(b.X + (c + 0.5f) * cw, b.Y + (r + 0.5f) * ch);
            kinds[r * cols + c] = flat.RoamArea.Contains(p) ? ZoneType.Ground : p.Y < flat.RoamArea.Top ? ZoneType.Sky : ZoneType.Blocked;
        }
        return new WalkMask(cols, rows, b, kinds);
    }

    // ---------------------------------------------------------------- file

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static HabitatEdits Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<HabitatEdits>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch
        {
            return new HabitatEdits();
        }
    }

    public void Write(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
