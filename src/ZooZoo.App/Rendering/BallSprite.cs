using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ZooZoo.Core.World;

namespace ZooZoo.Desktop.Rendering;

/// <summary>The fetch ball on the backyard canvas: a shaded red-and-white ball with its ground shadow.</summary>
public sealed class BallSprite : Control
{
    private const int Box = 120;
    private const float BallR = 9f;

    public double Scale { get; set; } = 1.0;
    private Ball? _ball;
    private float _depth = 1f;

    public BallSprite() => IsHitTestVisible = false;

    public void Place(Cell cell, Ball ball)
    {
        _ball = ball;
        _depth = cell.Map.DepthScale(ball.Position.Y);
        Width = Box * _depth / Scale;
        Height = Box * _depth / Scale;
        Canvas.SetLeft(this, (ball.Position.X - Box / 2f * _depth - cell.Bounds.X) / Scale);
        Canvas.SetTop(this, (ball.Position.Y - (Box - 16) * _depth - cell.Bounds.Y) / Scale);
        ZIndex = (int)ball.Position.Y + 2;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_ball is null) return;
        using var _ = context.PushTransform(Matrix.CreateScale(_depth / Scale, _depth / Scale));

        var groundY = Box - 16.0;
        var cx = Box / 2.0;
        var lift = _ball.Height * 0.91;
        var shadowScale = Math.Max(0.4, 1 - _ball.Height / 120.0);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(90 * shadowScale), 0, 0, 0)), null,
            new Point(cx, groundY), BallR * 1.1 * shadowScale, BallR * 0.4 * shadowScale);

        var c = new Point(cx, groundY - BallR - lift);
        var ball = new RadialGradientBrush
        {
            Center = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
            Radius = 0.8,
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xFF, 0x9A, 0x8C), 0),
                new GradientStop(Color.FromRgb(0xE2, 0x3B, 0x34), 0.55),
                new GradientStop(Color.FromRgb(0x8E, 0x1C, 0x1A), 1),
            },
        };
        context.DrawEllipse(ball, null, c, BallR, BallR);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 2.2), new Point(c.X - BallR * 0.95, c.Y + 1), new Point(c.X + BallR * 0.95, c.Y - 1));
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), null, new Point(c.X - BallR * 0.35, c.Y - BallR * 0.4), BallR * 0.22, BallR * 0.16);
    }
}
