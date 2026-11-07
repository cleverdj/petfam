using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ZooZoo.Core.Geometry;
using ZooZoo.Core.Pets;
using ZooZoo.Core.World;

namespace ZooZoo.Desktop.Rendering;

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

    public PetSprite(Pet pet)
    {
        Pet = pet;
        IsHitTestVisible = false;
    }

    /// <summary>Canvas position (DIPs) that puts the feet at the pet's pixel position inside the cell.</summary>
    public void Place(Cell cell)
    {
        Depth = cell.Map.DepthScale(Pet.Position.Y);
        Width = BoxWidth * Depth / Scale;
        Height = BoxHeight * Depth / Scale;
        Canvas.SetLeft(this, (Pet.Position.X - FootX * Depth - cell.Bounds.X) / Scale);
        Canvas.SetTop(this, (Pet.Position.Y - FootY * Depth - cell.Bounds.Y) / Scale);
        ZIndex = (int)Pet.Position.Y;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var _ = context.PushTransform(Matrix.CreateScale(Depth / Scale, Depth / Scale));
        Paint(context, Pet, Depth);
    }

    /// <summary>GPU 3D renderer when available, vector renderer otherwise. Draws in sprite-box model units.</summary>
    public static void Paint(DrawingContext context, Pet pet, float depth)
    {
        // Inside its house: only the "z z" (and plumbob/bubble) show, at the door.
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
