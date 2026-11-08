using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Desktop.Rendering;

/// <summary>
/// One small control per pet, positioned on a Canvas. Keeping each pet in its own visual means a frame
/// only dirties a ~220x200 px box instead of the whole monitor (plan §8.4 performance budget).
/// The box is scaled by the habitat's depth scale so pets higher on screen look further away, and
/// ZIndex follows the feet Y so nearer pets draw over farther ones.
/// </summary>
public sealed class PetSprite : Control
{
    /// <summary>Sprite box in pixels at depth scale 1. Feet sit at (FootX, FootY) inside the box.</summary>
    public const int BoxWidth = 220;
    public const int BoxHeight = 200;
    public const int FootX = 110;
    public const int FootY = 170;

    public Pet Pet { get; }

    /// <summary>Render scaling of the host window (pixels per DIP).</summary>
    public double Scale { get; set; } = 1.0;

    public float Depth { get; private set; } = 1f;

    /// <summary>Set while the pet is inside a house the user built: its door (base middle) and the house's depth scale.</summary>
    public (Vec2 Door, float Depth)? Home { get; set; }

    public PetSprite(Pet pet)
    {
        Pet = pet;
        IsHitTestVisible = false;
    }

    /// <summary>Canvas position (DIPs) that puts the feet at the pet's pixel position inside the cell.</summary>
    public void Place(Cell cell)
    {
        if (Home is { } home && Pet.Hidden)
        {
            // Lying just inside the door, drawn over the house and cut to the door opening.
            Depth = home.Depth;
            var feet = new Vec2(home.Door.X, home.Door.Y - InHouseBack * home.Depth);
            Width = BoxWidth * Depth / Scale;
            Height = BoxHeight * Depth / Scale;
            Canvas.SetLeft(this, (feet.X - FootX * Depth - cell.Bounds.X) / Scale);
            Canvas.SetTop(this, (feet.Y - FootY * Depth - cell.Bounds.Y) / Scale);
            ZIndex = (int)home.Door.Y + 1;
            return;
        }
        Depth = cell.Map.DepthScale(Pet.Position.Y);
        Width = BoxWidth * Depth / Scale;
        Height = BoxHeight * Depth / Scale;
        Canvas.SetLeft(this, (Pet.Position.X - FootX * Depth - cell.Bounds.X) / Scale);
        Canvas.SetTop(this, (Pet.Position.Y - FootY * Depth - cell.Bounds.Y) / Scale);
        // Going in or out of a house: drawn over the house, cut to the doorway.
        ZIndex = Pet.Doorway is { } dw ? (int)dw.Door.Y + 1 : (int)(Pet.Position.Y + Pet.ZBoost);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var _ = context.PushTransform(Matrix.CreateScale(Depth / Scale, Depth / Scale));
        if (Home is not null && Pet.Hidden)
        {
            PaintInHouse(context);
            return;
        }
        if (Pet.Doorway is { } door)
        {
            PaintInDoorway(context, door);
            return;
        }
        Paint(context, Pet, Depth);
        if (Pet.Butterfly is { } b)
            PetRenderer.DrawButterfly(context, new Point(FootX + (b.X - Pet.Position.X) / Depth, FootY + (b.Y - Pet.Position.Y) / Depth), Pet.AnimationTime);
    }

    /// <summary>How far behind the threshold the lying pet's feet are (house units).</summary>
    private const float InHouseBack = 9f;

    /// <summary>
    /// Inside the house: the pet lies in the doorway facing out, sized to the door (its head about fills the opening),
    /// with only what is inside the door opening showing — the chin and front paws rest on the threshold.
    /// Sprite units, feet at (FootX, FootY), drawn at the house's depth.
    /// </summary>
    private void PaintInHouse(DrawingContext context)
    {
        var d = PetRenderer.DimsFor(Pet);
        var s = Math.Clamp(HouseArt.DoorHalfWidth * 2 * 0.95 / (d.HeadR * 2.5), 0.35, 1.0);
        var dx = (double)FootX;
        var dy = FootY + InHouseBack;                            // threshold line
        var w = HouseArt.DoorHalfWidth;
        var opening = new StreamGeometry();
        using (var g = opening.Open())
        {
            g.BeginFigure(new Point(dx - w, dy + 1), true);
            g.LineTo(new Point(dx - w, dy - HouseArt.DoorStraight));
            g.ArcTo(new Point(dx + w, dy - HouseArt.DoorStraight), new Size(w, w), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(dx + w, dy + 1));
            g.EndFigure(true);
        }
        using (context.PushGeometryClip(opening))
        using (context.PushTransform(Matrix.CreateTranslation(-FootX, -FootY) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(FootX, FootY)))
            Paint(context, Pet, Depth, inside: true);
        // "z z" and speech bubbles are not cut by the door.
        PetRenderer.DrawOverlays(context, Pet, new Vec2(FootX, FootY - (float)(HouseArt.DoorHeight * 0.6)));
    }

    /// <summary>
    /// A pet walking into (or out of) a house: it shrinks a little toward the door as it walks up (a door is smaller
    /// than a dog), and once its feet pass the threshold everything outside the door opening is hidden by the wall.
    /// Sprite units: feet at (FootX, FootY), depth 1.
    /// </summary>
    private void PaintInDoorway(DrawingContext context, Doorway door)
    {
        var k = door.HouseDepth / Depth;                       // house units → this sprite's units
        var dx = (door.Door.X - Pet.Position.X) / Depth + FootX;
        var dy = (door.Door.Y - Pet.Position.Y) / Depth + FootY;

        // How far the pet is from lining up in front of the door (1) to through it (0).
        var front = PetFam.Core.Behavior.Behaviors.SleepBehavior.FrontOfDoor * k;
        var outside = Math.Clamp((FootY - dy) / front, 0, 1);  // feet below the threshold
        var fit = Math.Min(1.0, HouseArt.DoorHeight * k * 0.92 / PetRenderer.StandingHeight(Pet));
        var s = fit + (1 - fit) * outside;

        IDisposable? clip = null;
        if (FootY < dy + 3)
        {
            // In front of the wall: everything below the threshold; behind it: only the door opening.
            var w = HouseArt.DoorHalfWidth * k;
            var straight = HouseArt.DoorStraight * k;
            var opening = new StreamGeometry();
            using (var g = opening.Open())
            {
                g.BeginFigure(new Point(dx - w, dy + 2), true);
                g.LineTo(new Point(dx - w, dy - straight));
                g.ArcTo(new Point(dx + w, dy - straight), new Size(w, w), 0, false, SweepDirection.Clockwise);
                g.LineTo(new Point(dx + w, dy + 2));
                g.EndFigure(true);
            }
            var below = new RectangleGeometry(new Rect(-1000, dy + 2, 4000, 2000));
            clip = context.PushGeometryClip(new CombinedGeometry(GeometryCombineMode.Union, opening, below));
        }
        using (context.PushTransform(Matrix.CreateTranslation(-FootX, -FootY) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(FootX, FootY)))
            Paint(context, Pet, Depth);
        clip?.Dispose();
    }

    /// <summary>GPU 3D renderer when available, vector renderer otherwise. Draws in sprite-box model units.</summary>
    public static void Paint(DrawingContext context, Pet pet, float depth, bool inside = false)
    {
        if (inside)
        {
            if (SdfPetRenderer.Enabled) SdfPetRenderer.PaintBody(context, pet, depth, FootX, FootY, BoxWidth, BoxHeight);
            else PetRenderer.DrawBodyOnly(context, pet, new Vec2(FootX, FootY), depth);
            return;
        }

        // Inside a house recognised in the wallpaper: only the "z z" (and plumbob/bubble) show, at the door.
        if (pet.Hidden)
        {
            PetRenderer.DrawOverlays(context, pet, new Vec2(FootX, FootY));
            return;
        }

        if (SdfPetRenderer.Enabled)
            SdfPetRenderer.Paint(context, pet, depth, FootX, FootY, BoxWidth, BoxHeight);
        else
            PetRenderer.DrawBody(context, pet, new Vec2(FootX, FootY), depth);
    }
}
