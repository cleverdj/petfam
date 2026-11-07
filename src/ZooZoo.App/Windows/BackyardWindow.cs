using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ZooZoo.Core;
using ZooZoo.Core.Pets;
using ZooZoo.Core.Platform;
using ZooZoo.Core.World;
using ZooZoo.Desktop.Rendering;

namespace ZooZoo.Desktop.Windows;

/// <summary>
/// 뒷마당 layer: lives between the wallpaper and every app window (plan §3). On Windows it is reparented
/// into WorkerW. Transparent by default; set ZOOZOO_PAINT_WALLPAPER=1 to paint the wallpaper into the
/// window as a fallback if the desktop layer ever shows up black.
/// </summary>
public sealed class BackyardWindow : Window
{
    private readonly Panel _root = new();
    private readonly Image _wallpaperImage = new();
    private readonly Canvas _canvas = new();
    private readonly Dictionary<Guid, PetSprite> _sprites = new();
    private readonly Dictionary<Guid, BowlSprite> _bowls = new();
    private readonly BallSprite _ball = new() { IsVisible = false };

    private Cell? _cell;
    private Simulation? _sim;
    private IPlatform? _platform;
    private IWindowLayering? _layering;
    private IntPtr _handle;

    public Visual RootVisual => _root;

    public BackyardWindow()
    {
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.Manual;

        _root.Children.Add(_wallpaperImage);
        _root.Children.Add(_canvas);
        Content = _root;
        Opened += OnOpened;
    }

    public void Configure(Screen screen, Cell cell, Simulation sim, IPlatform platform, IWindowLayering layering,
        Bitmap? wallpaper, WallpaperFit fit)
    {
        _cell = cell;
        _sim = sim;
        _platform = platform;
        _layering = layering;

        Position = new PixelPoint(cell.Bounds.X, cell.Bounds.Y);
        Width = cell.Bounds.Width / screen.Scaling;
        Height = cell.Bounds.Height / screen.Scaling;

        ConfigureWallpaper(wallpaper, fit);
    }

    /// <summary>Called once per frame by the host: syncs sprite positions and invalidates only the sprites.</summary>
    public void RenderFrame()
    {
        if (_cell is null || _sim is null) return;
        var scale = RenderScaling;

        foreach (var pet in _sim.Pets)
        {
            if (!_sprites.TryGetValue(pet.Id, out var sprite))
            {
                sprite = new PetSprite(pet);
                _sprites[pet.Id] = sprite;
                _canvas.Children.Add(sprite);
            }

            var visible = pet.CellId == _cell.Id && pet.Layer == PetLayer.Backyard;
            if (sprite.IsVisible != visible) sprite.IsVisible = visible;
            if (!visible) continue;

            sprite.Scale = scale;
            sprite.Place(_cell);
            sprite.InvalidateVisual();
        }

        // Fetch ball.
        if (!_canvas.Children.Contains(_ball)) _canvas.Children.Add(_ball);
        var ball = _cell.Ball;
        if (_ball.IsVisible != (ball is not null)) _ball.IsVisible = ball is not null;
        if (ball is not null)
        {
            _ball.Scale = scale;
            _ball.Place(_cell, ball);
            _ball.InvalidateVisual();
        }

        // Bowls: add new, drop removed, keep the rest in place.
        foreach (var bowl in _cell.Bowls)
        {
            if (!_bowls.TryGetValue(bowl.Id, out var bs))
            {
                bs = new BowlSprite(bowl);
                _bowls[bowl.Id] = bs;
                _canvas.Children.Add(bs);
            }
            bs.Scale = scale;
            bs.Place(_cell);
            bs.InvalidateVisual();
        }
        foreach (var id in _bowls.Keys.Where(id => _cell.Bowls.All(b => b.Id != id)).ToList())
        {
            _canvas.Children.Remove(_bowls[id]);
            _bowls.Remove(id);
        }
    }

    private void ConfigureWallpaper(Bitmap? wallpaper, WallpaperFit fit)
    {
        if (wallpaper is null)
        {
            _wallpaperImage.IsVisible = false;
            return;
        }

        _wallpaperImage.Source = wallpaper;
        _wallpaperImage.HorizontalAlignment = HorizontalAlignment.Stretch;
        _wallpaperImage.VerticalAlignment = VerticalAlignment.Stretch;
        _wallpaperImage.ClipToBounds = true;
        _wallpaperImage.Stretch = fit switch
        {
            WallpaperFit.Stretch => Stretch.Fill,
            WallpaperFit.Fit => Stretch.Uniform,
            WallpaperFit.Center or WallpaperFit.Tile => Stretch.None,
            _ => Stretch.UniformToFill,
        };
        if (fit is WallpaperFit.Fit or WallpaperFit.Center or WallpaperFit.Tile)
            _root.Background = Brushes.Black;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_handle == IntPtr.Zero || _cell is null) return;

        AppLog.Write($"Backyard hwnd=0x{_handle.ToInt64():X}");
        _platform?.RegisterOwnWindow(_handle);

        if (Environment.GetEnvironmentVariable("ZOOZOO_NO_ATTACH") == "1")
        {
            AppLog.Write("ZOOZOO_NO_ATTACH=1: backyard stays a normal window");
            return;
        }

        try
        {
            _layering?.AttachToDesktopLayer(_handle, _cell.Bounds);
            AppLog.Write("Backyard attached to desktop layer");
        }
        catch (Exception ex)
        {
            AppLog.Write("Desktop layer attach failed, staying a normal window: " + ex);
        }

        // Inside WorkerW the toolkit may re-apply its own size after the DPI changes. Size in the scaling we
        // actually got, then force the native pixel bounds back to the cell, and verify a moment later.
        Relayout();
        ScalingChanged += (_, _) => Relayout();
        DispatcherTimer.RunOnce(() => AppLog.Write("Backyard native: " + _layering?.DescribeWindow(_handle)), TimeSpan.FromSeconds(1));
    }

    private void Relayout()
    {
        if (_cell is null) return;
        var scale = RenderScaling;
        Width = _cell.Bounds.Width / scale;
        Height = _cell.Bounds.Height / scale;
        _layering?.SetDesktopLayerBounds(_handle, _cell.Bounds);
        AppLog.Write($"Backyard relayout: renderScaling={scale:F2} size={Width:F0}x{Height:F0} dip, native {_layering?.DescribeWindow(_handle)}");
    }
}
