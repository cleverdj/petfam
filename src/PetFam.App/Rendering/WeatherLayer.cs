using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PetFam.Core;
using PetFam.Core.Geometry;
using PetFam.Core.World;

namespace PetFam.Desktop.Rendering;

/// <summary>
/// The weather over the desktop, in the backyard layer (behind every app window, over the wallpaper and the pets):
/// a darker sky when it is overcast, rain streaks with little splashes on the floor, drifting snowflakes and snow
/// lying on the floor, fog, and lightning flashes. Particles are a pure function of time (no state), so a frame
/// costs one geometry for the rain and a few hundred small ellipses at most.
/// </summary>
public sealed class WeatherLayer : Control
{
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    private Simulation? _sim;
    private Cell? _cell;
    private HabitatMap? _floorFor;
    private readonly List<Vec2> _floor = new();

    public double Scale { get; set; } = 1.0;

    /// <summary>True: only the snow lying on the floor (drawn under the pets and houses). False: everything else, over them.</summary>
    public bool Ground { get; }

    public WeatherLayer(bool ground = false)
    {
        Ground = ground;
        IsHitTestVisible = false;
    }

    public void Configure(Simulation sim, Cell cell)
    {
        _sim = sim;
        _cell = cell;
    }

    /// <summary>Anything to draw at all.</summary>
    public bool HasContent => _sim is { } s && (Ground
        ? s.SnowCover > 0.01f
        : s.Weather.Kind != WeatherKind.Clear || s.SinceLightning < 0.6f || s.Daylight < 0.995f);

    /// <summary>Needs redrawing every frame (moving particles or a flash), not just now and then.</summary>
    public bool Animating => !Ground && _sim is { } s && (s.Weather.Raining || s.Weather.Snowing || s.SinceLightning < 0.6f);

    private static float Hash(int i)
    {
        var h = (uint)i * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>Spots on the floor (for splashes and snow), re-sampled when the habitat map changes.</summary>
    private void SampleFloor(Cell cell)
    {
        if (ReferenceEquals(_floorFor, cell.Map)) return;
        _floorFor = cell.Map;
        _floor.Clear();
        var r = cell.Map.RoamArea;
        for (var i = 0; _floor.Count < 160 && i < 4000; i++)
        {
            var p = new Vec2(r.Left + Hash(i * 3 + 1) * r.Width, r.Top + Hash(i * 3 + 2) * r.Height);
            if (cell.Map.CanRoam(p)) _floor.Add(p);
        }
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        if (_sim is null || _cell is null) return;
        var w = _sim.Weather;
        var b = _cell.Bounds;
        var t = Clock.Elapsed.TotalSeconds;
        SampleFloor(_cell);

        // Pixel coordinates of the monitor.
        using var _ = ctx.PushTransform(Matrix.CreateTranslation(-b.X, -b.Y) * Matrix.CreateScale(1 / Scale, 1 / Scale));
        var W = (double)b.Width;
        var H = (double)b.Height;
        var screen = new Rect(b.X, b.Y, W, H);
        if (Ground)
        {
            DrawSnowCover(ctx);
            return;
        }

        // Sky: overcast darkens the desktop a little; fog is a pale haze rising from the ground.
        var shade = w.Kind switch
        {
            WeatherKind.Cloudy => 0.10 * w.Intensity,
            WeatherKind.Rain => 0.12 + 0.12 * w.Intensity,
            WeatherKind.Thunder => 0.30,
            WeatherKind.Snow => 0.06,
            _ => 0.0,
        };
        if (shade > 0) ctx.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * shade), 0x28, 0x32, 0x46)), screen);
        DrawNight(ctx, screen, w);
        if (w.Kind == WeatherKind.Fog)
        {
            var fog = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(150 * w.Intensity), 0xEE, 0xF0, 0xF2), 0),
                    new GradientStop(Color.FromArgb((byte)(60 * w.Intensity), 0xEE, 0xF0, 0xF2), 0.6),
                    new GradientStop(Color.FromArgb(0, 0xEE, 0xF0, 0xF2), 1),
                },
            };
            ctx.FillRectangle(fog, screen);
        }

        if (w.Raining) DrawRain(ctx, w, b, W, H, t);
        if (w.Snowing) DrawSnow(ctx, w, b, W, H, t);

        // Lightning: a bright flash and a weaker second one.
        var since = _sim.SinceLightning;
        if (since < 0.6f)
        {
            var a = Math.Max(0, 1 - since / 0.12) * 0.55 + (since is > 0.22f and < 0.36f ? 0.3 * (1 - (since - 0.22) / 0.14) : 0);
            if (a > 0.01) ctx.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * a), 0xF4, 0xF6, 0xFF)), screen);
        }
    }

    /// <summary>Snow lying on the floor, under everything that stands on it.</summary>
    private void DrawSnowCover(DrawingContext ctx)
    {
        if (_sim!.SnowCover > 0.01f && _floor.Count > 0)
        {
            var n = (int)(_floor.Count * Math.Min(1f, _sim.SnowCover * 1.2f));
            var snow = new SolidColorBrush(Color.FromArgb((byte)(200 * Math.Min(1f, _sim.SnowCover + 0.2f)), 0xF8, 0xFB, 0xFF));
            for (var i = 0; i < n; i++)
            {
                var p = _floor[i];
                var depth = _cell.Map.DepthScale(p.Y);
                var rx = (34 + 46 * Hash(i + 500)) * depth * (0.6 + 0.4 * _sim.SnowCover);
                ctx.DrawEllipse(snow, null, new Point(p.X, p.Y), rx, rx * 0.22);
            }
        }
    }

    /// <summary>
    /// Night falls on the desktop with the real clock: a deep blue dusk over everything, a warm glow low in the sky
    /// around sunrise and sunset, and stars in the sky above the floor on clear nights.
    /// </summary>
    private void DrawNight(DrawingContext ctx, Rect screen, WeatherNow w)
    {
        var daylight = _sim!.Daylight;
        if (daylight >= 0.995f) return;
        var night = 1f - daylight;
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * 0.34 * night), 0x0E, 0x16, 0x3A)), screen);

        // Twilight: strongest halfway between day and night.
        var glow = 4f * daylight * (1f - daylight);
        if (glow > 0.02f)
        {
            var warm = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(70 * glow), 0xFF, 0x9A, 0x50), 0),
                    new GradientStop(Color.FromArgb((byte)(40 * glow), 0xFF, 0x7A, 0x6A), 0.5),
                    new GradientStop(Color.FromArgb(0, 0xFF, 0x7A, 0x6A), 1),
                },
            };
            ctx.FillRectangle(warm, screen);
        }

        // Stars on clear (or lightly cloudy) nights, in the sky above the floor.
        if (night > 0.5f && (w.Kind == WeatherKind.Clear || w.Kind == WeatherKind.Cloudy && w.Intensity < 0.5f))
        {
            var skyBottom = Math.Max(screen.Y + 60, Math.Min(_cell!.Map.RoamArea.Top - 20, screen.Y + screen.Height * 0.55));
            var count = w.Kind == WeatherKind.Clear ? 110 : 45;
            var t = Clock.Elapsed.TotalSeconds;
            for (var i = 0; i < count; i++)
            {
                var x = screen.X + Hash(i * 11 + 5) * screen.Width;
                var y = screen.Y + Hash(i * 11 + 6) * (skyBottom - screen.Y);
                var twinkle = 0.55 + 0.45 * Math.Sin(t * (0.6 + Hash(i * 11 + 7)) + i);
                var size = 0.9 + 1.6 * Hash(i * 11 + 8);
                var a = (byte)(230 * (night - 0.5f) * 2 * twinkle);
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, 0xFF, 0xFA, 0xE6)), null, new Point(x, y), size, size);
            }
        }
    }

    private void DrawRain(DrawingContext ctx, WeatherNow w, RectI b, double W, double H, double t)
    {
        var count = (int)(110 + 380 * w.Intensity) + (w.Kind == WeatherKind.Thunder ? 120 : 0);
        var len = 16 + 26 * w.Intensity;
        const double slant = 0.18;
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            for (var i = 0; i < count; i++)
            {
                var speed = 1300 + 700 * Hash(i * 7 + 3);
                var y = (Hash(i * 7 + 1) * (H + len) + t * speed) % (H + len) - len;
                var x = ((Hash(i * 7 + 2) * (W + H * slant) - y * slant) % (W + H * slant) + W + H * slant) % (W + H * slant) - H * slant * 0.5;
                gc.BeginFigure(new Point(b.X + x, b.Y + y), false);
                gc.LineTo(new Point(b.X + x - len * slant, b.Y + y + len));
            }
        }
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(110 + 60 * w.Intensity), 0xD6, 0xE4, 0xF4)), 1.9, lineCap: PenLineCap.Round), g);

        // Splashes: little rings on the floor.
        if (_floor.Count == 0) return;
        var splashes = (int)(24 + 60 * w.Intensity);
        var ring = new Pen(new SolidColorBrush(Color.FromArgb(150, 0xE6, 0xEE, 0xF8)), 1.1);
        for (var i = 0; i < splashes; i++)
        {
            const double Period = 0.75;
            var phase = (t / Period + Hash(i + 900)) % 1.0;
            // A different floor spot each cycle.
            var cycle = (int)Math.Floor(t / Period + Hash(i + 900));
            var p = _floor[(int)(Hash(i * 31 + cycle * 977) * _floor.Count) % _floor.Count];
            var depth = _cell!.Map.DepthScale(p.Y);
            var r = (2 + 11 * phase) * depth;
            var alpha = (byte)(170 * (1 - phase));
            ring = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0xE6, 0xEE, 0xF8)), 1.1);
            ctx.DrawEllipse(null, ring, new Point(p.X, p.Y), r, r * 0.32);
        }
    }

    private static void DrawSnow(DrawingContext ctx, WeatherNow w, RectI b, double W, double H, double t)
    {
        var count = (int)(70 + 230 * w.Intensity);
        var flake = new SolidColorBrush(Color.FromArgb(225, 0xFF, 0xFF, 0xFF));
        var halo = new SolidColorBrush(Color.FromArgb(60, 0xFF, 0xFF, 0xFF));
        for (var i = 0; i < count; i++)
        {
            var size = 1.6 + 3.2 * Hash(i * 5 + 4);
            var speed = 35 + 25 * size;                       // bigger flakes are nearer and fall faster
            var y = (Hash(i * 5 + 1) * (H + 20) + t * speed) % (H + 20) - 10;
            var sway = Math.Sin(t * (0.5 + Hash(i * 5 + 3)) + i) * (10 + 18 * Hash(i * 5 + 2));
            var x = (Hash(i * 5 + 2) * W + sway + t * 12) % W;
            var c = new Point(b.X + x, b.Y + y);
            if (size > 3.4) ctx.DrawEllipse(halo, null, c, size * 1.8, size * 1.8);
            ctx.DrawEllipse(flake, null, c, size, size);
        }
    }
}
