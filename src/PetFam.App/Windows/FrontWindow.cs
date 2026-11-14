using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PetFam.Core;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.Platform;
using PetFam.Core.World;
using PetFam.Desktop.Rendering;

namespace PetFam.Desktop.Windows;

/// <summary>
/// Front-yard layer for one pet: a small topmost, non-activating window that appears when the cursor is over a
/// visible pet. It receives mouse input (unlike the overlay): hover = the pet looks at you, click = Sims-style
/// pie menu, press and hold = stroking, drag = carry. Plan §7.1.
/// </summary>
public sealed class FrontWindow : Window
{
    private const double DragThreshold = 8;
    private const double StrokeHoldSeconds = 0.35;

    private readonly Pet _pet;
    private readonly Simulation _sim;
    private readonly Cell _startCell;
    private readonly IPlatform _platform;
    private readonly IWindowLayering _layering;
    private readonly Action<Pet> _openMenu;
    private readonly FrontPetControl _control;

    private bool _pressed;
    private bool _carrying;
    private bool _stroking;
    private Vec2 _pressAt;
    private DateTime _pressTime;
    private IPointer? _pointer;

    public FrontWindow(Pet pet, Simulation sim, Cell cell, IPlatform platform, IWindowLayering layering, Action<Pet> openMenu)
    {
        _pet = pet;
        _sim = sim;
        _startCell = cell;
        _platform = platform;
        _layering = layering;
        _openMenu = openMenu;

        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.Manual;

        _control = new FrontPetControl(pet) { Cursor = new Cursor(StandardCursorType.Hand) };
        _control.PointerPressed += OnPointerPressed;
        _control.PointerMoved += OnPointerMoved;
        _control.PointerReleased += OnPointerReleased;
        Content = _control;

        Opened += (_, _) =>
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;
            _platform.RegisterOwnWindow(handle);
            try { _layering.MakeFrontWindow(handle); }
            catch (Exception ex) { AppLog.Write("Front window style failed: " + ex.Message); }
        };
    }

    public bool IsHolding => _pressed;

    /// <summary>
    /// False while the pet is only hovered: the window is invisible and just takes the mouse, the pet stays drawn in
    /// the backyard. True while it is carried (drawn here, above other windows).
    /// </summary>
    public bool DrawPet
    {
        get => _control.DrawPet;
        set => _control.DrawPet = value;
    }

    /// <summary>The window is big enough for the pet up to this depth scale (at this render scaling).</summary>
    private float _roomFor;
    private double _roomScale;

    /// <summary>The cell the pet is in now (it may have walked over to another monitor since this window was made).</summary>
    private Cell _cell => _sim.World.Find(_pet.CellId) ?? _startCell;

    /// <summary>Per frame: follow the pet (or the cursor while carrying) and redraw.</summary>
    public void Follow()
    {
        var depth = _cell.Map.DepthScale(_pet.Position.Y);
        var scale = RenderScaling;
        var pos = new PixelPoint((int)(_pet.Position.X - PetSprite.FootX * depth), (int)(_pet.Position.Y - PetSprite.FootY * depth));
        if (Position != pos) Position = pos;
        // Resizing a transparent window flickers, and a pet walking up or down the screen changes size every frame.
        // So the window only ever grows (with room to spare); the pet is drawn at its size inside it.
        if (depth > _roomFor || scale != _roomScale)
        {
            _roomFor = depth * 1.25f;
            _roomScale = scale;
            Width = PetSprite.BoxWidth * _roomFor / scale;
            Height = PetSprite.BoxHeight * _roomFor / scale;
        }
        _control.Depth = depth;
        _control.Scale = scale;

        // Press without movement turns into stroking after a short hold.
        if (_pressed && !_carrying && !_stroking && (DateTime.UtcNow - _pressTime).TotalSeconds >= StrokeHoldSeconds)
        {
            _stroking = true;
            _sim.BeginStroke(_pet);
            _sim.StrokeMove(_pet, DateTime.UtcNow);
        }

        _control.InvalidateVisual();
    }

    private Vec2 ScreenPixels(PointerEventArgs e)
    {
        var p = this.PointToScreen(e.GetPosition(this));
        return new Vec2(p.X, p.Y);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_control);
        if (point.Properties.IsRightButtonPressed)
        {
            _openMenu(_pet);
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;

        _pressed = true;
        _carrying = false;
        _stroking = false;
        _pressAt = ScreenPixels(e);
        _pressTime = DateTime.UtcNow;
        _pointer = e.Pointer;
        e.Pointer.Capture(_control);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressed) return;
        var cur = ScreenPixels(e);

        if (!_carrying && !_stroking && Vec2.Distance(cur, _pressAt) > DragThreshold)
        {
            _carrying = true;
            _sim.BeginCarry(_pet);
            AppLog.Write($"Carry begin: {_pet.Name}");
        }

        if (_carrying)
        {
            var depth = _cell.Map.DepthScale(_pet.Position.Y);
            var grab = PetRenderer.GrabOffset(_pet.Species);
            _sim.UpdateCarry(_pet, cur, new Vec2(grab.X * depth, grab.Y * depth));
        }
        else if (_stroking)
        {
            _sim.StrokeMove(_pet, DateTime.UtcNow);
        }
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        _pointer?.Capture(null);
        _pointer = null;

        if (_carrying)
        {
            _sim.EndCarry(_pet, _pet.Position);
            _pet.SetAnimation("idle");
            AppLog.Write($"Carry end: {_pet.Name} at {_pet.Position}");
        }
        else if (_stroking)
        {
            _sim.EndStroke(_pet);
        }
        else
        {
            // A plain click opens the Sims-style pie menu.
            _openMenu(_pet);
        }

        _carrying = false;
        _stroking = false;
        e.Handled = true;
    }

}

/// <summary>Draws the pet inside the front window using the same renderer as the backyard sprite.</summary>
public sealed class FrontPetControl : Control
{
    private readonly Pet _pet;

    public float Depth { get; set; } = 1f;
    public double Scale { get; set; } = 1.0;

    public FrontPetControl(Pet pet)
    {
        _pet = pet;
        // A transparent background still hit-tests the whole box so presses land anywhere on the pet.
        Background = Brushes.Transparent;
    }

    public IBrush? Background { get; set; }

    public bool DrawPet { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Takes the mouse over the pet's own box only, not the spare room the window keeps for growing.
        if (Background is not null)
            context.FillRectangle(Background, new Rect(0, 0, PetSprite.BoxWidth * Depth / Scale, PetSprite.BoxHeight * Depth / Scale));
        if (!DrawPet) return;
        using var _ = context.PushTransform(Matrix.CreateScale(Depth / Scale, Depth / Scale));
        PetSprite.Paint(context, _pet, Depth);
    }
}
