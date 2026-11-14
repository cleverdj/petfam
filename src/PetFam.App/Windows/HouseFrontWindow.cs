using PetFam.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PetFam.Core.Geometry;
using PetFam.Core.Platform;
using PetFam.Core.World;
using PetFam.Desktop.Rendering;

namespace PetFam.Desktop.Windows;

/// <summary>
/// An invisible topmost window over a built house while the cursor is on it: click to decorate it,
/// drag to move it somewhere else on the floor. The house itself stays drawn in the backyard.
/// </summary>
public sealed class HouseFrontWindow : Window
{
    private const double DragThreshold = 6;
    /// <summary>A press held longer than this is not a click, even if the mouse hardly moved.</summary>
    private const double ClickSeconds = 0.5;

    private readonly Cell _cell;
    private readonly IPlatform _platform;
    private readonly IWindowLayering _layering;
    private readonly Action<PetHouse> _onClick;
    private readonly Action<PetHouse> _onMoved;
    private readonly Border _hit;

    private PetHouse? _house;
    private bool _pressed, _dragging;
    private Vec2 _pressAt, _grab;
    private DateTime _pressTime;
    private double _farthest;
    private int _w, _h;
    private double _scale;
    private IPointer? _pointer;

    public HouseFrontWindow(Cell cell, IPlatform platform, IWindowLayering layering, Action<PetHouse> onClick, Action<PetHouse> onMoved)
    {
        _cell = cell;
        _platform = platform;
        _layering = layering;
        _onClick = onClick;
        _onMoved = onMoved;

        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.Manual;

        // A transparent fill still takes the mouse, so the whole house body is clickable.
        _hit = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        ToolTip.SetTip(_hit, L.S("Click to decorate · drag to move", "눌러서 꾸미기 · 끌어서 옮기기"));
        _hit.PointerPressed += OnPressed;
        _hit.PointerMoved += OnMoved;
        _hit.PointerReleased += OnReleased;
        // Losing the mouse mid-drag still counts as a move (never as a click).
        _hit.PointerCaptureLost += (_, _) =>
        {
            if (_pressed && _dragging && _house is not null) _onMoved(_house);
            _pressed = false;
            _dragging = false;
        };
        Content = _hit;

        Opened += (_, _) =>
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;
            _platform.RegisterOwnWindow(handle);
            try { _layering.MakeFrontWindow(handle); }
            catch (Exception ex) { AppLog.Write("House window style failed: " + ex.Message); }
        };
    }

    public bool IsHolding => _pressed;
    public PetHouse? House => _house;

    /// <summary>Screen rectangle of a house body, in pixels.</summary>
    public static RectI BodyOnScreen(Cell cell, PetHouse h)
    {
        var d = cell.Map.DepthScale(h.Position.Y);
        var r = HouseArt.BodyRect;
        return new RectI((int)(h.Position.X + r.X * d), (int)(h.Position.Y + r.Y * d), (int)(r.Width * d), (int)(r.Height * d));
    }

    public void Follow(PetHouse house)
    {
        if (!ReferenceEquals(_house, house) && !_pressed) _house = house;
        if (_house is null) return;
        var body = BodyOnScreen(_cell, _house);
        var scale = RenderScaling;
        var pos = new PixelPoint(body.X, body.Y);
        if (Position != pos) Position = pos;
        // Resizing every frame while a house is dragged up or down the screen can drop the mouse; only when it changes.
        if (body.Width != _w || body.Height != _h || scale != _scale)
        {
            _w = body.Width;
            _h = body.Height;
            _scale = scale;
            Width = body.Width / scale;
            Height = body.Height / scale;
        }
    }

    private Vec2 ScreenPixels(PointerEventArgs e)
    {
        var p = this.PointToScreen(e.GetPosition(this));
        return new Vec2(p.X, p.Y);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_house is null || !e.GetCurrentPoint(_hit).Properties.IsLeftButtonPressed) return;
        _pressed = true;
        _dragging = false;
        _pressAt = ScreenPixels(e);
        _pressTime = DateTime.UtcNow;
        _farthest = 0;
        _grab = _house.Position - _pressAt;
        _pointer = e.Pointer;
        e.Pointer.Capture(_hit);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressed || _house is null) return;
        var cur = ScreenPixels(e);
        _farthest = Math.Max(_farthest, Vec2.Distance(cur, _pressAt));
        if (!_dragging && _farthest > DragThreshold)
        {
            _dragging = true;
            _hit.Cursor = new Cursor(StandardCursorType.SizeAll);
        }
        if (_dragging) _house.Position = _cell.Map.ClampToRoam(cur + _grab);
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed || _house is null) return;
        var cur = ScreenPixels(e);
        _farthest = Math.Max(_farthest, Vec2.Distance(cur, _pressAt));
        var dragged = _dragging || _farthest > DragThreshold;
        var quick = (DateTime.UtcNow - _pressTime).TotalSeconds < ClickSeconds;
        _pressed = false;
        _dragging = false;
        _pointer?.Capture(null);
        _pointer = null;
        _hit.Cursor = new Cursor(StandardCursorType.Hand);
        // Only a quick click without moving opens the decorating window; moving it never does.
        if (dragged) _onMoved(_house);
        else if (quick) _onClick(_house);
        e.Handled = true;
    }
}
