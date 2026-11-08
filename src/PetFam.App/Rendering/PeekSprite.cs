using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;

namespace PetFam.Desktop.Rendering;

/// <summary>
/// A pet peeking out from behind a window edge, on the overlay canvas. The real pet (same 3D model and face as on the
/// floor) is drawn with its head at the peek point and its body behind the window, tilted toward the edge it leans out
/// of (upside down from the bottom edge); everything over the window is clipped away, so only the face shows. It looks
/// straight at the user.
/// Draws in absolute pixel coordinates (so the window-rect clip stays correct) translated into its own box.
/// </summary>
public sealed class PeekSprite : Control
{
    public const int Box = 240;          // pixels
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

    /// <summary>Tilt (degrees, clockwise) so the body goes behind the window: lean out sideways, hang from the bottom.</summary>
    public static double Tilt(PeekEdge edge) => edge switch
    {
        PeekEdge.Left => -38,
        PeekEdge.Right => 38,
        PeekEdge.Bottom => 180,
        _ => 0,
    };

    /// <summary>Head size on screen: a bit bigger than on the floor so the face reads at a window edge.</summary>
    public static double ScaleFor(Pet pet) => Math.Clamp(PetRenderer.PeekHeadRadius * 0.95 / PetRenderer.DimsFor(pet).HeadR, 1.0, 1.7);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var s = Pet.Peek;
        if (s is null) return;
        using var toBox = context.PushTransform(Matrix.CreateTranslation(-_origin.X, -_origin.Y) * Matrix.CreateScale(1 / Scale, 1 / Scale));

        // Hide whatever is over the window: the body stays behind it.
        var r = s.WindowRect;
        var clip = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(-100000, -100000, 200000, 200000)),
            new RectangleGeometry(new Rect(r.X, r.Y, r.Width, r.Height)));
        using var clipped = context.PushGeometryClip(clip);

        var (center, _) = PetRenderer.PeekHeadPose(s);
        var head = PetRenderer.HeadSpot(Pet);
        var k = ScaleFor(Pet);
        var angle = Tilt(s.Edge) * Math.PI / 180;

        // Sprite units (feet at FootX/FootY) → head at the origin → scaled, tilted → at the peek point.
        var m = Matrix.CreateTranslation(-(PetSprite.FootX + head.X), -(PetSprite.FootY + head.Y))
                * Matrix.CreateScale(k, k)
                * Matrix.CreateRotation(angle)
                * Matrix.CreateTranslation(center.X, center.Y);

        // It peeks out to look at you: facing front, head turned to the viewer (the renderer's FaceViewer).
        using var placed = context.PushTransform(m);
        PetSprite.Paint(context, Pet, 1f);
    }
}
