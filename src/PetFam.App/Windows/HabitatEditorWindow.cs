using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PetFam.Core;
using PetFam.Core.Geometry;
using PetFam.Core.Platform;
using PetFam.Core.World;

namespace PetFam.Desktop.Windows;

/// <summary>
/// Habitat editor: covers one monitor with its wallpaper and what PetFam thinks each part of it is (floor, water,
/// mud, sand, sunny spots, shade), and lets the user paint corrections with a round brush. Works on a copy of the
/// strokes; Save hands them back. The pets keep living underneath and pick the new floor up on Save.
/// </summary>
public sealed class HabitatEditorWindow : Window
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x40));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x5A, 0x64, 0x74));

    /// <summary>Paint colours of each kind (also the chip colours).</summary>
    public static Color ColorOf(ZoneType k) => k switch
    {
        ZoneType.Ground => Color.FromRgb(0x4C, 0xC2, 0x5A),
        ZoneType.Water => Color.FromRgb(0x2E, 0x8C, 0xF0),
        ZoneType.Mud => Color.FromRgb(0x8A, 0x5A, 0x2E),
        ZoneType.Sand => Color.FromRgb(0xE8, 0xC8, 0x6A),
        ZoneType.Sun => Color.FromRgb(0xFF, 0xD2, 0x2E),
        ZoneType.Hide => Color.FromRgb(0x3A, 0x6E, 0x6A),
        ZoneType.House => Color.FromRgb(0xC8, 0x6A, 0xD8),
        _ => Color.FromRgb(0xE0, 0x40, 0x40),
    };

    public static string NameOf(ZoneType? k) => k switch
    {
        ZoneType.Ground => L.S("Floor", "바닥"),
        ZoneType.Water => L.S("Water", "물"),
        ZoneType.Mud => L.S("Mud", "진흙"),
        ZoneType.Sand => L.S("Sand", "모래"),
        ZoneType.Sun => L.S("Sunny spot", "양지"),
        ZoneType.Hide => L.S("Shade", "그늘"),
        ZoneType.Blocked => L.S("Not floor", "바닥 아님"),
        _ => L.S("Undo (back to automatic)", "되돌리기 (자동으로)"),
    };

    private readonly RectI _bounds;
    private readonly WalkMask _auto;
    private readonly List<HabitatStroke> _strokes;
    private readonly MaskView _view;
    private readonly List<(Border Chip, ZoneType? Kind)> _chips = new();
    private ZoneType? _tool = ZoneType.Ground;
    private float _radius = 0.03f;          // share of the screen width
    private bool _painting;

    public HabitatEditorWindow(Screen screen, RectI bounds, WalkMask auto, IReadOnlyList<HabitatStroke> strokes, Bitmap? wallpaper,
        WallpaperFit fit, string where, Action<IReadOnlyList<HabitatStroke>> onSave)
    {
        _bounds = bounds;
        _auto = auto;
        _strokes = strokes.ToList();
        Title = L.S("Fix the habitat", "서식지 고치기");
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(bounds.X, bounds.Y);
        Width = bounds.Width / screen.Scaling;
        Height = bounds.Height / screen.Scaling;
        FontFamily = PieMenuWindow.UiFont;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x2C));

        _view = new MaskView(this) { Wallpaper = wallpaper, Fit = fit };
        _view.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_view).Properties.IsLeftButtonPressed) return;
            _painting = true;
            e.Pointer.Capture(_view);
            Dab(e.GetPosition(_view));
        };
        _view.PointerMoved += (_, e) =>
        {
            _view.Cursor = new Cursor(StandardCursorType.Cross);
            _view.Brush = e.GetPosition(_view);
            if (_painting) Dab(e.GetPosition(_view));
            _view.InvalidateVisual();
        };
        _view.PointerReleased += (_, e) => { _painting = false; e.Pointer.Capture(null); };
        _view.PointerExited += (_, _) => { _view.Brush = null; _view.InvalidateVisual(); };
        _view.PointerWheelChanged += (_, e) =>
        {
            _radius = Math.Clamp(_radius * (e.Delta.Y > 0 ? 1.15f : 1 / 1.15f), 0.008f, 0.12f);
            _view.InvalidateVisual();
        };

        // Tool bar, floating at the top.
        var barWidth = Math.Min(900, bounds.Width / screen.Scaling - 40);
        var tools = new WrapPanel { Width = barWidth - 28 };
        foreach (var k in HabitatEdits.Paintable) tools.Children.Add(Chip(k));
        tools.Children.Add(Chip(null));

        var size = new Slider { Minimum = 0.008, Maximum = 0.12, Value = _radius, Width = 160, VerticalAlignment = VerticalAlignment.Center };
        size.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) _radius = (float)size.Value; };
        var clear = new Button { Content = L.S("Start over", "처음부터"), Padding = new Thickness(12, 6), CornerRadius = new CornerRadius(10) };
        var armed = false;
        clear.Click += (_, _) =>
        {
            if (!armed) { armed = true; clear.Content = L.S("Remove all my fixes? Click again", "고친 것을 모두 지울까요? 한 번 더"); return; }
            armed = false;
            clear.Content = L.S("Start over", "처음부터");
            _strokes.Clear();
            Refresh();
        };
        var undo = new Button { Content = L.S("Undo", "실행 취소"), Padding = new Thickness(12, 6), CornerRadius = new CornerRadius(10) };
        undo.Click += (_, _) => { UndoStroke(); };
        var cancel = new Button { Content = L.S("Cancel", "취소"), Padding = new Thickness(14, 6), CornerRadius = new CornerRadius(10) };
        cancel.Click += (_, _) => Close();
        var save = new Button
        {
            Content = L.S("Save", "저장"), Background = PieMenuWindow.SimsBlue, Foreground = Brushes.White, FontWeight = FontWeight.Bold,
            Padding = new Thickness(18, 6), CornerRadius = new CornerRadius(10),
        };
        save.Click += (_, _) => { onSave(_strokes); Close(); };

        var row2 = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children =
            {
                new TextBlock { Text = L.S("Brush", "붓 크기"), Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 },
                size, undo, clear, cancel, save,
            },
        };
        var hint = new TextBlock
        {
            Text = L.S($"{where}: paint where your pets can walk, swim or nap. Green is floor; uncoloured parts are not floor. "
                       + "Mouse wheel: brush size. Esc: cancel.",
                $"{where}: 펫이 걷고, 헤엄치고, 낮잠 잘 곳을 칠해 주세요. 초록색이 바닥이고, 색이 없는 곳은 바닥이 아니에요. "
                + "마우스 휠: 붓 크기. Esc: 취소."),
            Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Width = barWidth - 28,
        };
        var bar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xF7, 0xF9, 0xFC)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Width = barWidth,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Color = Color.FromArgb(0x60, 0, 0, 0), OffsetY = 4 }),
            Child = new StackPanel { Spacing = 8, Children = { hint, tools, row2 } },
        };

        Content = new Panel { Children = { _view, bar } };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control)) UndoStroke();
        };
        Refresh();
    }

    /// <summary>The mask shown: the automatic one with the strokes so far.</summary>
    public WalkMask Current { get; private set; } = null!;

    public float Radius => _radius;

    public RectI ScreenBounds => _bounds;

    private void Dab(Point p)
    {
        var w = Math.Max(1, _view.Bounds.Width);
        var h = Math.Max(1, _view.Bounds.Height);
        var s = new HabitatStroke((float)(p.X / w), (float)(p.Y / h), _radius, _tool);
        // Skip dabs right on top of the last one (a held mouse sends many).
        if (_strokes.Count > 0 && _strokes[^1] is var last && last.Kind == s.Kind && Math.Abs(last.R - s.R) < 1e-4
            && Math.Pow((last.X - s.X) * w, 2) + Math.Pow((last.Y - s.Y) * h, 2) < Math.Pow(_radius * w * 0.25, 2)) return;
        _strokes.Add(s);
        Refresh();
    }

    private void UndoStroke()
    {
        if (_strokes.Count == 0) return;
        // A stroke is many dabs: undo back to where the brush kind or size last changed, at most 60 dabs.
        var last = _strokes[^1];
        var n = 0;
        while (_strokes.Count > 0 && n < 60 && _strokes[^1].Kind == last.Kind && Math.Abs(_strokes[^1].R - last.R) < 1e-4)
        {
            _strokes.RemoveAt(_strokes.Count - 1);
            n++;
        }
        Refresh();
    }

    private void Refresh()
    {
        Current = HabitatEdits.Apply(_auto, _strokes);
        foreach (var (chip, kind) in _chips)
        {
            var on = kind == _tool;
            chip.BorderBrush = on ? PieMenuWindow.SimsBlue : new SolidColorBrush(Color.FromRgb(0xD6, 0xDD, 0xE8));
            chip.Background = on ? new SolidColorBrush(Color.FromRgb(0xE4, 0xEE, 0xFC)) : Brushes.White;
        }
        _view.InvalidateVisual();
    }

    private Border Chip(ZoneType? kind)
    {
        var dot = new Border
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(7),
            Background = kind is { } k ? new SolidColorBrush(ColorOf(k)) : Brushes.White,
            BorderBrush = Muted, BorderThickness = new Thickness(kind is null ? 1 : 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var chip = new Border
        {
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(2),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { dot, new TextBlock { Text = NameOf(kind), FontSize = 13, Foreground = Ink } },
            },
        };
        chip.PointerPressed += (_, e) => { _tool = kind; Refresh(); e.Handled = true; };
        _chips.Add((chip, kind));
        return chip;
    }

    /// <summary>The wallpaper as Windows shows it, the current mask in colour over it, and the brush.</summary>
    private sealed class MaskView : Control
    {
        private readonly HabitatEditorWindow _owner;
        public Bitmap? Wallpaper { get; init; }
        public WallpaperFit Fit { get; init; }
        public Point? Brush { get; set; }

        public MaskView(HabitatEditorWindow owner) => _owner = owner;

        public override void Render(DrawingContext ctx)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0x30, 0x34, 0x3C)), new Rect(0, 0, w, h));
            if (Wallpaper is { } bmp)
            {
                var src = new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
                var dest = Fit switch
                {
                    WallpaperFit.Stretch => new Rect(0, 0, w, h),
                    WallpaperFit.Fit => Uniform(src.Size, w, h, fill: false),
                    WallpaperFit.Center or WallpaperFit.Tile => new Rect((w - src.Width) / 2, (h - src.Height) / 2, src.Width, src.Height),
                    _ => Uniform(src.Size, w, h, fill: true),
                };
                using (ctx.PushClip(new Rect(0, 0, w, h)))
                    ctx.DrawImage(bmp, src, dest);
            }

            var mask = _owner.Current;
            var cw = w / mask.Cols;
            var ch = h / mask.Rows;
            var brushes = new Dictionary<ZoneType, IBrush>();
            for (var r = 0; r < mask.Rows; r++)
            for (var c = 0; c < mask.Cols; c++)
            {
                var k = mask.Kind(c, r);
                if (k is ZoneType.Blocked or ZoneType.Sky) continue;
                if (!brushes.TryGetValue(k, out var b))
                {
                    var col = ColorOf(k);
                    b = brushes[k] = new SolidColorBrush(Color.FromArgb(k == ZoneType.Ground ? (byte)0x60 : (byte)0x90, col.R, col.G, col.B));
                }
                ctx.FillRectangle(b, new Rect(c * cw, r * ch, cw + 0.6, ch + 0.6));
            }

            if (Brush is { } p)
            {
                var rad = _owner.Radius * w;
                ctx.DrawEllipse(null, new Pen(Brushes.White, 2), p, rad, rad);
                ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), 1), p, rad + 1.5, rad + 1.5);
            }
        }

        private static Rect Uniform(Size img, double w, double h, bool fill)
        {
            var k = fill ? Math.Max(w / img.Width, h / img.Height) : Math.Min(w / img.Width, h / img.Height);
            var dw = img.Width * k;
            var dh = img.Height * k;
            return new Rect((w - dw) / 2, (h - dh) / 2, dw, dh);
        }
    }
}
