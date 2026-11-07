using SkiaSharp;
using ZooZoo.Core.Geometry;
using ZooZoo.Core.Platform;
using ZooZoo.Core.World;

namespace ZooZoo.Desktop.Rendering;

/// <summary>
/// Reads the wallpaper the way Windows lays it out on the monitor (fill / fit / stretch / center), samples it into
/// a coarse colour grid and hands that to <see cref="HabitatAnalyzer"/> to find the floor.
/// </summary>
public static class WallpaperAnalyzer
{
    public const int Cols = 128;
    private const int Sub = 4; // pixels per cell edge in the sampled image

    public static HabitatAnalyzer.Result? Analyze(string path, WallpaperFit fit, RectI bounds, RectI workingArea)
    {
        using var src = SKBitmap.Decode(path);
        if (src is null) return null;

        var rows = Math.Max(20, (int)Math.Round(Cols * bounds.Height / (double)bounds.Width));
        var sw = Cols * Sub;
        var sh = rows * Sub;
        var k = sw / (double)bounds.Width;

        double W = bounds.Width, H = bounds.Height, iw = src.Width, ih = src.Height;
        SKRect dest;
        switch (fit)
        {
            case WallpaperFit.Stretch:
                dest = new SKRect(0, 0, sw, sh);
                break;
            case WallpaperFit.Fit:
            {
                var s = Math.Min(W / iw, H / ih);
                dest = Centered(iw * s, ih * s);
                break;
            }
            case WallpaperFit.Center:
            case WallpaperFit.Tile:
                dest = Centered(iw, ih);
                break;
            default:
            {
                var s = Math.Max(W / iw, H / ih);
                dest = Centered(iw * s, ih * s);
                break;
            }
        }

        SKRect Centered(double w, double h) => SKRect.Create((float)((W - w) / 2 * k), (float)((H - h) / 2 * k), (float)(w * k), (float)(h * k));

        using var sampled = new SKBitmap(new SKImageInfo(sw, sh, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(sampled))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(src, dest, paint);
        }

        var cells = new Lab[Cols * rows];
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < Cols; c++)
        {
            int R = 0, G = 0, B = 0;
            for (var y = 0; y < Sub; y++)
            for (var x = 0; x < Sub; x++)
            {
                var px = sampled.GetPixel(c * Sub + x, r * Sub + y);
                R += px.Red; G += px.Green; B += px.Blue;
            }
            const int n = Sub * Sub;
            cells[r * Cols + c] = Lab.FromRgb((byte)(R / n), (byte)(G / n), (byte)(B / n));
        }

        return HabitatAnalyzer.Analyze(cells, Cols, rows, bounds, workingArea);
    }
}
