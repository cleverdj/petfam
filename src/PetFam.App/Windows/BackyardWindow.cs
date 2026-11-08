using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PetFam.Core;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;
using PetFam.Desktop.Rendering;

namespace PetFam.Desktop.Windows;

/// <summary>
/// Backyard layer: lives between the wallpaper and every app window (plan §3). On Windows it is reparented
/// into WorkerW. Transparent by default; set PETFAM_PAINT_WALLPAPER=1 to paint the wallpaper into the
/// window as a fallback if the desktop layer ever shows up black.
/// </summary>
public sealed class BackyardWindow : Window
{
    private readonly Panel _root = new();
    private readonly Image _wallpaperImage = new();
    private readonly Canvas _canvas = new();
    private readonly Dictionary<Guid, PetSprite> _sprites = new();
    private readonly Dictionary<Guid, BowlSprite> _bowls = new();
    private readonly Dictionary<Guid, HouseSprite> _houses = new();
    private readonly BallSprite _ball = new() { IsVisible = false };
    private readonly WeatherLayer _weather = new() { IsVisible = false };
    private readonly WeatherLayer _snowGround = new(ground: true) { IsVisible = false };
    private double _sinceWeatherDraw;
    private string _weatherKey = "";
    private string _groundKey = "";

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
        _root.Children.Add(_snowGround);   // snow lying on the floor: under the pets and houses
        _root.Children.Add(_canvas);
        _root.Children.Add(_weather);   // over the pets: rain falls in front of them
        Content = _root;
        Opened += OnOpened;
    }

    /// <summary>The user switched virtual desktops: draw that desktop's cell (same monitor, so same bounds).</summary>
    public void ShowCell(Cell cell)
    {
        _cell = cell;
        if (_sim is not null) { _weather.Configure(_sim, cell); _snowGround.Configure(_sim, cell); }
    }

    public void Configure(Screen screen, Cell cell, Simulation sim, IPlatform platform, IWindowLayering layering,
        Bitmap? wallpaper, WallpaperFit fit)
    {
        _cell = cell;
        _sim = sim;
        _platform = platform;
        _layering = layering;
        _weather.Configure(sim, cell);
        _snowGround.Configure(sim, cell);

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
            sprite.Home = pet.Hidden && pet.InsideHouse is { } inside && _cell.Houses.FirstOrDefault(h => h.Id == inside) is { } home
                ? (home.Position, _cell.Map.DepthScale(home.Position.Y))
                : null;
            if (sprite.IsVisible != visible) sprite.IsVisible = visible;
            if (!visible) continue;

            sprite.Scale = scale;
            sprite.Place(_cell);
            sprite.InvalidateVisual();
        }

        // Pets that were sent away.
        foreach (var id in _sprites.Keys.Where(id => _sim.Pets.All(p => p.Id != id)).ToList())
        {
            _canvas.Children.Remove(_sprites[id]);
            _sprites.Remove(id);
        }

        // Houses the user built.
        foreach (var house in _cell.Houses)
        {
            if (!_houses.TryGetValue(house.Id, out var hs))
            {
                hs = new HouseSprite(house);
                _houses[house.Id] = hs;
                _canvas.Children.Add(hs);
            }
            hs.Scale = scale;
            var home = _sim.Pets.Where(p => p.CellId == _cell.Id && p.Hidden &&
                                            (p.InsideHouse == house.Id || p.InsideHouse is null && Vec2.Distance(p.Position, house.Position) < 40f)).ToList();
            hs.Occupied = home.Count > 0;
            hs.Snow = _sim.SnowCover;
            hs.Occupants = home.Count == 0 ? null : home.Select(p =>
            {
                var c = PetRenderer.CoatOf(p);
                return new HouseArt.Occupant(p.Name, c.Fur, c.Belly, c.Dark, c.Nose, c.EarInner, p.Species == PetSpecies.Cat,
                    Asleep: p.Engine.Current is PetFam.Core.Behavior.Behaviors.SleepBehavior);
            }).ToList();
            hs.Place(_cell);
            hs.InvalidateVisual();
        }
        foreach (var id in _houses.Keys.Where(id => _cell.Houses.All(h => h.Id != id)).ToList())
        {
            _canvas.Children.Remove(_houses[id]);
            _houses.Remove(id);
        }

        // Fetch ball.
        if (!_canvas.Children.Contains(_ball)) _canvas.Children.Add(_ball);
        var ball = _cell.Ball;
        var showBall = ball is not null && !ball.InFront;
        if (_ball.IsVisible != showBall) _ball.IsVisible = showBall;
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

        // Weather: every frame while something falls, otherwise only when it changes (or once a second).
        _weather.Scale = scale;
        var show = _weather.HasContent;
        if (_weather.IsVisible != show) _weather.IsVisible = show;
        if (show)
        {
            var key = $"{_sim.Weather.Kind}{_sim.Weather.Intensity}{_sim.SnowCover:F2}{_cell.Id}";
            _sinceWeatherDraw += 1;
            // Rain and snow redraw at 15 fps (every other frame): they cover the whole screen, so each redraw
            // repaints everything under them; falling streaks still look smooth at that rate.
            if ((_weather.Animating && _sinceWeatherDraw >= 2) || key != _weatherKey || _sinceWeatherDraw > 30)
            {
                _weatherKey = key;
                _sinceWeatherDraw = 0;
                _weather.InvalidateVisual();
                _snowGround.InvalidateVisual();
            }
        }
        _snowGround.Scale = scale;
        var ground = _snowGround.HasContent;
        if (_snowGround.IsVisible != ground) _snowGround.IsVisible = ground;
        var groundKey = $"{_sim.SnowCover:F2}{_cell.Id}{_cell.Map.GetHashCode()}";
        if (ground && groundKey != _groundKey)
        {
            _groundKey = groundKey;
            _snowGround.InvalidateVisual();
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

        if (Environment.GetEnvironmentVariable("PETFAM_NO_ATTACH") == "1")
        {
            AppLog.Write("PETFAM_NO_ATTACH=1: backyard stays a normal window");
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
