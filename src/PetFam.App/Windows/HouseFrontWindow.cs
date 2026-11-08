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

    private readonly Cell _cell;
    private readonly IPlatform _platform;
    private readonly IWindowLayering _layering;
    private readonly Action<PetHouse> _onClick;
    private readonly Action<PetHouse> _onMoved;
    private readonly Border _hit;

    private PetHouse? _house;
    private bool _pressed, _dragging;
    private Vec2 _pressAt, _grab;
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
        _hit.PointerCaptureLost += (_, _) => { _pressed = false; _dragging = false; };
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
        Position = new PixelPoint(body.X, body.Y);
        Width = body.Width / scale;
        Height = body.Height / scale;
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
        _grab = _house.Position - _pressAt;
        _pointer = e.Pointer;
        e.Pointer.Capture(_hit);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressed || _house is null) return;
        var cur = ScreenPixels(e);
        if (!_dragging && Vec2.Distance(cur, _pressAt) > DragThreshold)
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
        _pressed = false;
        _pointer?.Capture(null);
        _pointer = null;
        _hit.Cursor = new Cursor(StandardCursorType.Hand);
        if (_dragging) _onMoved(_house);
        else _onClick(_house);
        _dragging = false;
        e.Handled = true;
    }
}
