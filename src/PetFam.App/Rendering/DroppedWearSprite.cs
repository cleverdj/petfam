using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PetFam.Core.World;

namespace PetFam.Desktop.Rendering;

/// <summary>A hat or pair of glasses lying on the floor (shaken off, or dropped by a dog), drawn as a small flat icon.</summary>
public sealed class DroppedWearSprite : Control
{
    public const int BoxWidth = 70;
    public const int BoxHeight = 44;

    public DroppedWear Item { get; }
    public double Scale { get; set; } = 1.0;
    private float _depth = 1f;

    public DroppedWearSprite(DroppedWear item)
    {
        Item = item;
        IsHitTestVisible = false;
    }

    public void Place(Cell cell)
    {
        _depth = cell.Map.DepthScale(Item.Position.Y) * 0.7f;
        Width = BoxWidth * _depth / Scale;
        Height = BoxHeight * _depth / Scale;
        Canvas.SetLeft(this, (Item.Position.X - BoxWidth / 2f * _depth - cell.Bounds.X) / Scale);
        Canvas.SetTop(this, (Item.Position.Y - (BoxHeight - 6) * _depth - cell.Bounds.Y) / Scale);
        ZIndex = (int)Item.Position.Y;
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        using var _ = ctx.PushTransform(Matrix.CreateScale(_depth / Scale, _depth / Scale));
        var col = BreedLook.C(Item.Piece.Color);
        var fill = new SolidColorBrush(col);
        var line = new Pen(new SolidColorBrush(Color.FromRgb((byte)(col.R * 0.45), (byte)(col.G * 0.45), (byte)(col.B * 0.45))), 2);
        var dark = new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x2A, 0x28)), 2);
        double cx = BoxWidth / 2.0, gy = BoxHeight - 6.0;
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), null, new Point(cx, gy), 26, 5);

        switch (Item.Piece.ItemId)
        {
            case "beanie":
            {
                var dome = new StreamGeometry();
                using (var g = dome.Open())
                {
                    g.BeginFigure(new Point(cx - 22, gy - 4), true);
                    g.ArcTo(new Point(cx + 22, gy - 4), new Size(22, 20), 0, false, SweepDirection.Clockwise);
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(fill, line, dome);
                ctx.DrawRectangle(new SolidColorBrush(Darken(col, 0.15)), line, new Rect(cx - 23, gy - 8, 46, 8), 3, 3);
                ctx.DrawEllipse(new SolidColorBrush(Lighten(col)), line, new Point(cx, gy - 26), 6, 6);
                break;
            }
            case "straw-hat":
            {
                var straw = new SolidColorBrush(Color.FromRgb(0xF0, 0xD5, 0x8A));
                var strawLine = new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x7A, 0x30)), 2);
                ctx.DrawEllipse(straw, strawLine, new Point(cx, gy - 4), 30, 8);
                ctx.DrawEllipse(straw, strawLine, new Point(cx, gy - 12), 14, 10);
                ctx.DrawRectangle(fill, null, new Rect(cx - 14, gy - 10, 28, 4));
                break;
            }
            case "crown":
            {
                var gold = new SolidColorBrush(Color.FromRgb(0xF0, 0xC4, 0x3C));
                var crown = new StreamGeometry();
                using (var g = crown.Open())
                {
                    g.BeginFigure(new Point(cx - 18, gy - 4), true);
                    g.LineTo(new Point(cx - 20, gy - 22));
                    g.LineTo(new Point(cx - 9, gy - 12));
                    g.LineTo(new Point(cx, gy - 26));
                    g.LineTo(new Point(cx + 9, gy - 12));
                    g.LineTo(new Point(cx + 20, gy - 22));
                    g.LineTo(new Point(cx + 18, gy - 4));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(gold, new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x70, 0x10)), 2), crown);
                ctx.DrawEllipse(fill, null, new Point(cx, gy - 9), 3.5, 3.5);
                break;
            }
            case "ribbon":
            {
                foreach (var s in new[] { -1, 1 })
                {
                    var bow = new StreamGeometry();
                    using (var g = bow.Open())
                    {
                        g.BeginFigure(new Point(cx, gy - 10), true);
                        g.LineTo(new Point(cx + s * 18, gy - 20));
                        g.LineTo(new Point(cx + s * 18, gy - 1));
                        g.EndFigure(true);
                    }
                    ctx.DrawGeometry(fill, line, bow);
                }
                ctx.DrawEllipse(new SolidColorBrush(Darken(col, 0.12)), line, new Point(cx, gy - 10), 4.5, 4.5);
                break;
            }
            case "flower":
                for (var i = 0; i < 5; i++)
                {
                    var a = i * Math.PI * 2 / 5 - Math.PI / 2;
                    ctx.DrawEllipse(fill, line, new Point(cx + Math.Cos(a) * 8, gy - 12 + Math.Sin(a) * 6), 6, 5);
                }
                ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xF6, 0xD2, 0x4A)), null, new Point(cx, gy - 12), 4, 3.5);
                break;
            case "glasses":
            case "sunglasses":
            {
                var lens = Item.Piece.ItemId == "sunglasses" ? new SolidColorBrush(Color.FromRgb(0x1E, 0x20, 0x26)) : null;
                var frame = new Pen(fill, 3);
                foreach (var s in new[] { -1, 1 }) ctx.DrawEllipse(lens, frame, new Point(cx + s * 12, gy - 9), 9, 7);
                ctx.DrawLine(frame, new Point(cx - 3, gy - 10), new Point(cx + 3, gy - 10));
                break;
            }
            default:
                ctx.DrawEllipse(fill, dark, new Point(cx, gy - 8), 16, 7);
                break;
        }
    }

    private static Color Darken(Color c, double k) => Color.FromRgb((byte)(c.R * (1 - k)), (byte)(c.G * (1 - k)), (byte)(c.B * (1 - k)));

    private static Color Lighten(Color c) => Color.FromRgb((byte)(c.R + (255 - c.R) * 0.6), (byte)(c.G + (255 - c.G) * 0.6), (byte)(c.B + (255 - c.B) * 0.6));
}
