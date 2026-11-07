using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ZooZoo.Core.Geometry;
using ZooZoo.Core.Pets;

namespace ZooZoo.Desktop.Rendering;

/// <summary>
/// Small control around a peeking head on the overlay canvas. Draws in absolute pixel coordinates
/// (so the window-rect clip stays correct) translated into its own box.
/// </summary>
public sealed class PeekSprite : Control
{
    public const int Box = 160;          // pixels
    private const int Half = Box / 2;

    public Pet Pet { get; }
    public double Scale { get; set; } = 1.0;
    public Vec2 Cursor { get; set; }

    private Point _origin; // absolute pixel position of the box's top-left corner

    public PeekSprite(Pet pet)
    {
        Pet = pet;
        IsHitTestVisible = false;
    }

    public void Place(RectI cellBounds)
    {
        if (Pet.Peek is null) return;
        var (center, _) = PetRenderer.PeekHeadPose(Pet.Peek);
        _origin = new Point(center.X - Half, center.Y - Half);
        Width = Box / Scale;
        Height = Box / Scale;
        Canvas.SetLeft(this, (_origin.X - cellBounds.X) / Scale);
        Canvas.SetTop(this, (_origin.Y - cellBounds.Y) / Scale);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Pet.Peek is null) return;
        using var _ = context.PushTransform(Matrix.CreateTranslation(-_origin.X, -_origin.Y) * Matrix.CreateScale(1 / Scale, 1 / Scale));
        PetRenderer.DrawPeek(context, Pet, Cursor);
    }
}
