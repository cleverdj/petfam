using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ZooZoo.Core.World;

namespace ZooZoo.Desktop.Rendering;

/// <summary>A food bowl on the backyard canvas. Origin is the bowl's bottom centre.</summary>
public sealed class BowlSprite : Control
{
    public const int BoxWidth = 72;
    public const int BoxHeight = 40;

    public FoodBowl Bowl { get; }
    public double Scale { get; set; } = 1.0;
    private float _depth = 1f;

    public BowlSprite(FoodBowl bowl)
    {
        Bowl = bowl;
        IsHitTestVisible = false;
    }

    public void Place(Cell cell)
    {
        _depth = cell.Map.DepthScale(Bowl.Position.Y);
        Width = BoxWidth * _depth / Scale;
        Height = BoxHeight * _depth / Scale;
        Canvas.SetLeft(this, (Bowl.Position.X - BoxWidth / 2f * _depth - cell.Bounds.X) / Scale);
        Canvas.SetTop(this, (Bowl.Position.Y - (BoxHeight - 6) * _depth - cell.Bounds.Y) / Scale);
        ZIndex = (int)Bowl.Position.Y + 1;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var _ = context.PushTransform(Matrix.CreateScale(_depth / Scale, _depth / Scale));
        var cx = BoxWidth / 2.0;
        var baseY = BoxHeight - 6.0;

        var bowl = new SolidColorBrush(Color.FromRgb(0x4E, 0x6E, 0xA8));
        var rim = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x44, 0x6E)), 1.6);
        var food = new SolidColorBrush(Color.FromRgb(0x9B, 0x6A, 0x3C));

        // Bowl body + rim.
        context.DrawEllipse(bowl, rim, new Point(cx, baseY - 2), 22, 6);
        context.FillRectangle(bowl, new Rect(cx - 22, baseY - 12, 44, 10));
        context.DrawLine(rim, new Point(cx - 22, baseY - 12), new Point(cx - 22, baseY - 2));
        context.DrawLine(rim, new Point(cx + 22, baseY - 12), new Point(cx + 22, baseY - 2));
        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x6A, 0x8C, 0xC8)), rim, new Point(cx, baseY - 12), 22, 6);

        // Food heap scaled by amount.
        var amount = Math.Clamp(Bowl.Amount, 0f, 1f);
        if (amount > 0.02f)
            context.DrawEllipse(food, null, new Point(cx, baseY - 13 - 3 * amount), 16 * (0.5 + 0.5 * amount), 4 + 4 * amount);
    }
}
