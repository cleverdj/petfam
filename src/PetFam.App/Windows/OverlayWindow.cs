using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using PetFam.Core;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;
using ZoneType = PetFam.Core.World.ZoneType;
using PetFam.Desktop.Rendering;

namespace PetFam.Desktop.Windows;

/// <summary>
/// Window-edge + front-yard layers: a topmost, click-through, non-activating transparent window over one monitor.
/// Hidden entirely when nothing is peeking and the HUD is off. Never blocks input (plan §5.3 rule 5).
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly Panel _root = new();
    private readonly Canvas _canvas = new();
    private readonly SceneControl _hud = new();
    private readonly Dictionary<Guid, PeekSprite> _sprites = new();

    private Cell? _cell;
    private Simulation? _sim;
    private IPlatform? _platform;
    private IWindowLayering? _layering;

    private int _frames;
    private double _fpsWindowStart;
    private double _fps;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public OverlayWindow()
    {
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.Manual;

        _hud.Paint = PaintHud;
        _hud.IsVisible = false;
        _root.Children.Add(_canvas);
        _root.Children.Add(_hud);
        Content = _root;
        Opened += OnOpened;
    }

    /// <summary>The user switched virtual desktops: draw that desktop's cell (same monitor, so same bounds).</summary>
    public void ShowCell(Cell cell) => _cell = cell;

    public void Configure(Screen screen, Cell cell, Simulation sim, IPlatform platform, IWindowLayering layering)
    {
        _cell = cell;
        _sim = sim;
        _platform = platform;
        _layering = layering;

        Position = new PixelPoint(cell.Bounds.X, cell.Bounds.Y);
        Width = cell.Bounds.Width / screen.Scaling;
        Height = cell.Bounds.Height / screen.Scaling;
    }

    /// <summary>True when there is something to draw: a peeking pet or the HUD.</summary>
    public bool HasContent =>
        _sim is not null && _cell is not null &&
        (_sim.Debug.ShowHud || _sim.Debug.ShowHabitat || _sim.Pets.Any(p => p.CellId == _cell.Id && p.Layer == PetLayer.WindowEdge));

    /// <summary>Per-frame sync: peek sprites follow their heads; HUD redraws only when enabled.</summary>
    public void RenderFrame()
    {
        if (_cell is null || _sim is null) return;
        var scale = RenderScaling;
        CountFrame();

        foreach (var pet in _sim.Pets)
        {
            if (!_sprites.TryGetValue(pet.Id, out var sprite))
            {
                sprite = new PeekSprite(pet);
                _sprites[pet.Id] = sprite;
                _canvas.Children.Add(sprite);
            }

            var visible = pet.CellId == _cell.Id && pet.Layer == PetLayer.WindowEdge && pet.Peek is not null;
            if (sprite.IsVisible != visible) sprite.IsVisible = visible;
            if (!visible) continue;

            sprite.Scale = scale;
            sprite.Cursor = _sim.User.Cursor;
            sprite.Place(_cell.Bounds);
            sprite.InvalidateVisual();
        }

        foreach (var id in _sprites.Keys.Where(id => _sim.Pets.All(p => p.Id != id)).ToList())
        {
            _canvas.Children.Remove(_sprites[id]);
            _sprites.Remove(id);
        }

        var hud = _sim.Debug.ShowHud || _sim.Debug.ShowHabitat;
        if (_hud.IsVisible != hud) _hud.IsVisible = hud;
        if (hud) _hud.InvalidateVisual();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        AppLog.Write($"Overlay hwnd=0x{handle.ToInt64():X}");
        _platform?.RegisterOwnWindow(handle);
        ApplyClickThrough();
    }

    /// <summary>Re-applied after every Show(): the window is hidden whenever it has nothing to draw.</summary>
    public void ApplyClickThrough()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        try
        {
            _layering?.MakeClickThroughOverlay(handle);
        }
        catch (Exception ex)
        {
            AppLog.Write("Overlay click-through failed: " + ex);
        }
    }

    private void CountFrame()
    {
        _frames++;
        var now = _clock.Elapsed.TotalSeconds;
        if (now - _fpsWindowStart >= 1.0)
        {
            _fps = _frames / (now - _fpsWindowStart);
            _frames = 0;
            _fpsWindowStart = now;
        }
    }

    private void PaintHud(DrawingContext ctx)
    {
        if (_cell is null || _sim is null) return;
        var scale = RenderScaling;
        var b = _cell.Bounds;
        using var _ = ctx.PushTransform(Matrix.CreateTranslation(-b.X, -b.Y) * Matrix.CreateScale(1 / scale, 1 / scale));

        if (_sim.Debug.ShowHabitat) PaintHabitat(ctx, scale);
        if (!_sim.Debug.ShowHud) return;

        var sb = new StringBuilder();
        var u = _sim.User;
        sb.AppendLine($"PetFam spike  {_fps:F0} fps  cell={_cell.Id}");
        sb.AppendLine($"foreground: {(u.Foreground is null ? "(none)" : $"{Trim(u.Foreground.Title, 40)} [{u.Foreground.ClassName}] {u.Foreground.Bounds}{(u.Foreground.IsMaximized ? " max" : "")}{(u.Foreground.IsFullscreen ? " FULL" : "")}")}");
        sb.AppendLine($"cursor={u.Cursor}  idle={u.Idle.TotalSeconds:F0}s  dnd={u.DoNotDisturb}  peekAlways={_sim.Debug.AllowPeekAlways}");
        foreach (var p in _sim.Pets)
        {
            var a = p.Appearance;
            sb.AppendLine(
                $"{p.Name}({p.Species}) {p.Engine.Current?.Name ?? "-"}/{p.Animation} layer={p.Layer} pos={p.Position} " +
                $"H{p.Needs.Hunger:F0} E{p.Needs.Energy:F0} F{p.Needs.Fun:F0} A{p.Needs.Affection:F0} C{p.Needs.Curiosity:F0} " +
                $"bond={p.Bond.Value:F1}({p.Bond.Stage}) wet={a.Wet:F2} mud={a.Mud:F2}{(a.MudDried ? "d" : "")}" +
                (p.Peek is null ? "" : $" peek={p.Peek.Edge}@{p.Peek.Progress:F2}"));
        }

        var text = new FormattedText(sb.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 13 * scale, Brushes.White);
        var origin = new Point(b.X + 12, b.Y + 12);
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
            new Rect(origin.X - 6, origin.Y - 6, text.Width + 12, text.Height + 12), 6);
        ctx.DrawText(text, origin);
    }

    /// <summary>Test view: recognised floor in green, the roam area outline, and the horizon used for depth.</summary>
    private void PaintHabitat(DrawingContext ctx, double scale)
    {
        var map = _cell!.Map;
        var label = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
        var font = new Typeface("Malgun Gothic");

        if (map.Mask is { } m)
        {
            for (var r = 0; r < m.Rows; r++)
            for (var c = 0; c < m.Cols; c++)
            {
                if (PlaceBrush(m.Kind(c, r)) is not { } fill) continue;
                var p = m.CellCenter(c, r);
                ctx.FillRectangle(fill, new Rect(p.X - m.CellW / 2, p.Y - m.CellH / 2, m.CellW + 0.5, m.CellH + 0.5));
            }
            DrawLegend(ctx, scale, m, font);
        }

        var roam = map.RoamArea;
        var color = map.Mask is null ? Color.FromRgb(0xFF, 0x9A, 0x3C) : Color.FromRgb(0xFF, 0xE0, 0x4A);
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(color), 2 * scale) { DashStyle = DashStyle.Dash }, new Rect(roam.X, roam.Y, roam.Width, roam.Height));
        ctx.DrawLine(new Pen(new SolidColorBrush(color), 3 * scale), new Point(roam.Left, roam.Top), new Point(roam.Right, roam.Top));

        var text = map.Mask is null ? L.S("No floor recognised: using a flat floor", "인식된 바닥 없음: 평평한 바닥으로 동작") : L.S($"Horizon (y={roam.Top})", $"지평선 (y={roam.Top})");
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, font, 14 * scale, label);
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), new Rect(roam.Left + 8 * scale, roam.Top - ft.Height - 10 * scale, ft.Width + 12 * scale, ft.Height + 6 * scale), 6);
        ctx.DrawText(ft, new Point(roam.Left + 14 * scale, roam.Top - ft.Height - 7 * scale));
    }

    private static readonly (ZoneType Kind, Loc Label, Color Color)[] Places =
    {
        (ZoneType.Ground, new Loc("Floor", "바닥"), Color.FromArgb(80, 60, 220, 90)),
        (ZoneType.Water, new Loc("Water", "물"), Color.FromArgb(110, 40, 120, 255)),
        (ZoneType.Mud, new Loc("Mud", "진흙"), Color.FromArgb(130, 120, 70, 30)),
        (ZoneType.Sun, new Loc("Sunny spot", "양지"), Color.FromArgb(110, 255, 200, 40)),
        (ZoneType.Sand, new Loc("Sand", "모래"), Color.FromArgb(110, 240, 220, 150)),
        (ZoneType.Hide, new Loc("Shade / shelter", "그늘·쉼터"), Color.FromArgb(110, 170, 90, 230)),
        (ZoneType.House, new Loc("House entrance", "집 입구"), Color.FromArgb(170, 255, 60, 140)),
    };

    private static IBrush? PlaceBrush(ZoneType kind)
    {
        foreach (var p in Places)
            if (p.Kind == kind) return new SolidColorBrush(p.Color);
        return null;
    }

    private void DrawLegend(DrawingContext ctx, double scale, WalkMask m, Typeface font)
    {
        var x = _cell!.Bounds.Right - 230 * scale;
        var y = _cell.Bounds.Y + 20 * scale;
        var line = 22 * scale;
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), new Rect(x - 10 * scale, y - 8 * scale, 220 * scale, (Places.Length * 22 + 12) * scale), 8);
        foreach (var (kind, label, color) in Places)
        {
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(230, color.R, color.G, color.B)), new Rect(x, y + 3 * scale, 14 * scale, 14 * scale), 3);
            var ft = new FormattedText(L.S($"{label}  {m.CountOf(kind)} cells", $"{label}  {m.CountOf(kind)}칸"), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, font, 13 * scale, Brushes.White);
            ctx.DrawText(ft, new Point(x + 22 * scale, y));
            y += line;
        }
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
