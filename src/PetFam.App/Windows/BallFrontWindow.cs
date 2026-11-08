using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PetFam.Core;
using PetFam.Core.Geometry;
using PetFam.Core.Platform;
using PetFam.Core.World;
using PetFam.Desktop.Rendering;

namespace PetFam.Desktop.Windows;

/// <summary>
/// The fetch ball in a small topmost window that takes mouse input, shown while the cursor is over the ball.
/// Press to pick the ball up, drag it around, and let go with a flick to throw it: the throw follows the
/// speed and direction of the last few mouse moves.
/// </summary>
public sealed class BallFrontWindow : Window
{
    /// <summary>Mouse samples older than this are ignored when measuring the flick.</summary>
    private const double FlickWindowSeconds = 0.09;

    private readonly Simulation _sim;
    private readonly Cell _cell;
    private readonly IPlatform _platform;
    private readonly IWindowLayering _layering;
    private readonly BallSprite _sprite;
    private readonly List<(double T, Vec2 P)> _samples = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    private Ball? _ball;
    private bool _pressed;
    private IPointer? _pointer;

    public BallFrontWindow(Simulation sim, Cell cell, IPlatform platform, IWindowLayering layering)
    {
        _sim = sim;
        _cell = cell;
        _platform = platform;
        _layering = layering;

        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.Manual;

        _sprite = new BallSprite(hitTest: true) { Cursor = new Cursor(StandardCursorType.Hand) };
        _sprite.PointerPressed += OnPointerPressed;
        _sprite.PointerMoved += OnPointerMoved;
        _sprite.PointerReleased += OnPointerReleased;
        _sprite.PointerCaptureLost += (_, _) => Release(Vec2.Zero);
        Content = _sprite;

        Opened += (_, _) =>
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;
            _platform.RegisterOwnWindow(handle);
            try { _layering.MakeFrontWindow(handle); }
            catch (Exception ex) { AppLog.Write("Ball window style failed: " + ex.Message); }
        };
    }

    public bool IsHolding => _pressed;

    /// <summary>Per frame: sit over the ball (or under the cursor while held) and redraw.</summary>
    public void Follow(Ball ball)
    {
        if (!ReferenceEquals(_ball, ball))
        {
            // The game ended or restarted while the ball was in hand.
            if (_pressed) Release(Vec2.Zero);
            _ball = ball;
        }

        var scale = RenderScaling;
        _sprite.Scale = scale;
        _sprite.Place(_cell, ball);
        var depth = _sprite.Depth;
        Width = BallSprite.Box * depth / scale;
        Height = BallSprite.Box * depth / scale;
        Position = new PixelPoint((int)(ball.Position.X - BallSprite.Box / 2f * depth), (int)(ball.Position.Y - BallSprite.GroundY * depth));
        _sprite.InvalidateVisual();
    }

    private Vec2 ScreenPixels(PointerEventArgs e)
    {
        var p = this.PointToScreen(e.GetPosition(this));
        return new Vec2(p.X, p.Y);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_ball is null || _ball.CarriedBy is not null) return;
        if (!e.GetCurrentPoint(_sprite).Properties.IsLeftButtonPressed) return;
        _pressed = true;
        _pointer = e.Pointer;
        e.Pointer.Capture(_sprite);
        _samples.Clear();
        _sim.BeginBallHold(_ball);
        var cur = ScreenPixels(e);
        _samples.Add((_clock.Elapsed.TotalSeconds, cur));
        _sim.UpdateBallHold(_cell, _ball, cur);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressed || _ball is null) return;
        var cur = ScreenPixels(e);
        var now = _clock.Elapsed.TotalSeconds;
        _samples.Add((now, cur));
        _samples.RemoveAll(s => now - s.T > 0.25);
        _sim.UpdateBallHold(_cell, _ball, cur);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed) return;
        var now = _clock.Elapsed.TotalSeconds;
        var cur = ScreenPixels(e);
        _samples.Add((now, cur));
        Release(Flick(now));
        e.Handled = true;
    }

    /// <summary>Mouse velocity over the last few samples, in screen pixels per second.</summary>
    private Vec2 Flick(double now)
    {
        var recent = _samples.Where(s => now - s.T <= FlickWindowSeconds).ToList();
        if (recent.Count < 2) return Vec2.Zero;
        var (t0, p0) = recent[0];
        var (t1, p1) = recent[^1];
        var span = t1 - t0;
        return span < 0.008 ? Vec2.Zero : (p1 - p0) * (float)(1 / span);
    }

    private void Release(Vec2 flick)
    {
        if (!_pressed) return;
        _pressed = false;
        var pointer = _pointer;
        _pointer = null;
        pointer?.Capture(null);
        if (_ball is { HeldByUser: true } ball)
        {
            _sim.ThrowBall(ball, flick);
            AppLog.Write($"Ball thrown: flick={flick} speed={flick.Length:F0}px/s");
        }
    }
}
