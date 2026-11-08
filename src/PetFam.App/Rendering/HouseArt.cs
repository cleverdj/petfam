using PetFam.Core;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PetFam.Core.World;

namespace PetFam.Desktop.Rendering;

/// <summary>
/// Draws a pet house in the cartoon style of the pets: flat colours, a brown outline, a round-topped door.
/// Coordinates are relative to the middle of the base (the doorstep), y up is negative, at depth scale 1.
/// </summary>
public static class HouseArt
{
    public static readonly (Loc Name, Color Color)[] RoofColors =
    {
        (new Loc("Red", "빨강"), Color.FromRgb(0xD9, 0x56, 0x4F)),
        (new Loc("Blue", "파랑"), Color.FromRgb(0x4A, 0x7F, 0xD0)),
        (new Loc("Green", "초록"), Color.FromRgb(0x4F, 0xA3, 0x5B)),
        (new Loc("Purple", "보라"), Color.FromRgb(0x8E, 0x6C, 0xC9)),
        (new Loc("Orange", "주황"), Color.FromRgb(0xE5, 0x8A, 0x3A)),
        (new Loc("Brown", "갈색"), Color.FromRgb(0x8A, 0x5A, 0x3C)),
    };

    public static readonly (Loc Name, Color Color)[] WallColors =
    {
        (new Loc("Cream", "크림"), Color.FromRgb(0xF6, 0xE7, 0xC8)),
        (new Loc("White", "하양"), Color.FromRgb(0xF7, 0xF5, 0xF0)),
        (new Loc("Sky", "하늘"), Color.FromRgb(0xCD, 0xE6, 0xF5)),
        (new Loc("Pink", "분홍"), Color.FromRgb(0xF7, 0xD3, 0xDC)),
        (new Loc("Mint", "민트"), Color.FromRgb(0xD2, 0xF0, 0xDF)),
        (new Loc("Yellow", "노랑"), Color.FromRgb(0xFB, 0xE7, 0xA1)),
    };

    public static readonly (HouseShape Shape, Loc Name)[] Shapes =
    {
        (HouseShape.Gable, new Loc("Gable roof", "세모 지붕")),
        (HouseShape.Round, new Loc("Round roof", "둥근 지붕")),
        (HouseShape.Tent, new Loc("Tent", "텐트")),
        (HouseShape.Cabin, new Loc("Log cabin", "통나무집")),
    };

    public static readonly (string Id, Loc Name)[] Decorations =
    {
        ("flowers", new Loc("Flower pots", "꽃 화분")),
        ("flags", new Loc("Bunting", "깃발 장식")),
        ("heart", new Loc("Heart", "하트 장식")),
        ("chimney", new Loc("Chimney", "굴뚝")),
        ("lamp", new Loc("Lantern", "등불")),
        ("fence", new Loc("Fence", "울타리")),
    };

    /// <summary>Box a house is drawn in (depth 1), with the base middle at (BoxWidth/2, BaseY).</summary>
    public const int BoxWidth = 240;
    public const int BoxHeight = 220;
    public const int BaseY = 200;

    /// <summary>Door opening (house units): half width, height of the straight sides, total height.</summary>
    public const double DoorHalfWidth = 16;
    public const double DoorStraight = 28;
    public const double DoorHeight = DoorStraight + DoorHalfWidth;

    /// <summary>A pet sleeping inside: its name and coat colours, for the face in the window and the sign.</summary>
    public sealed record Occupant(string Name, Color Fur, Color Belly, Color Dark, Color Nose, Color EarInner, bool Cat, bool Asleep = true);

    /// <summary>The clickable body of the house relative to the base middle (depth 1).</summary>
    public static Rect BodyRect => new(-72, -128, 144, 132);

    private static readonly Color Ink = Color.FromRgb(0x5A, 0x3A, 0x22);
    private static readonly Typeface SignFont = new("Malgun Gothic", FontStyle.Normal, FontWeight.Bold);

    public static Color Roof(PetHouse h) => RoofColors[Math.Clamp(h.RoofColor, 0, RoofColors.Length - 1)].Color;
    public static Color Wall(PetHouse h) => WallColors[Math.Clamp(h.WallColor, 0, WallColors.Length - 1)].Color;

    /// <param name="baseMiddle">Where the middle of the base goes.</param>
    /// <param name="time">Seconds, for chimney smoke and the lamp flicker.</param>
    /// <param name="occupied">A pet is sleeping inside: the window glows.</param>
    /// <param name="occupants">Who is sleeping inside: a sleepy face in the window and a name tag over the roof.</param>
    /// <param name="snow">0..1 snow lying on the roof.</param>
    public static void Draw(DrawingContext ctx, PetHouse h, Point baseMiddle, double scale, double time, bool occupied,
        IReadOnlyList<Occupant>? occupants = null, double snow = 0)
    {
        using var _ = ctx.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(baseMiddle.X, baseMiddle.Y));
        var ink = new Pen(new SolidColorBrush(Ink), 2.2, lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round);
        var thin = new Pen(new SolidColorBrush(Ink), 1.4, lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round);
        var roof = Roof(h);
        var wall = Wall(h);
        var deco = h.Decorations;

        // Ground shadow.
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, new Point(0, 2), 76, 11);

        if (deco.Contains("chimney") && h.Shape != HouseShape.Tent) Chimney(ctx, ink, roof, time, h.Shape);

        switch (h.Shape)
        {
            case HouseShape.Tent:
                Tent(ctx, ink, thin, roof, wall);
                break;
            case HouseShape.Round:
                Walls(ctx, ink, thin, wall, logs: false);
                Dome(ctx, ink, thin, roof);
                break;
            case HouseShape.Cabin:
                Walls(ctx, ink, thin, Mix(wall, Color.FromRgb(0xB0, 0x7A, 0x4A), 0.55), logs: true);
                Gable(ctx, ink, thin, roof);
                break;
            default:
                Walls(ctx, ink, thin, wall, logs: false);
                Gable(ctx, ink, thin, roof);
                break;
        }

        if (snow > 0.02) RoofSnow(ctx, h.Shape, snow);

        var tent = h.Shape == HouseShape.Tent;
        var home = occupants is { Count: > 0 } ? occupants : null;
        // Someone home: the window glows (the pet itself lies in the doorway, drawn by its sprite).
        if (!tent) Window(ctx, ink, thin, occupied || home is not null || deco.Contains("lamp"), null, time);
        Door(ctx, ink, thin, tent, null, time);
        if (deco.Contains("flags")) Flags(ctx, thin, h.Shape);
        if (deco.Contains("heart")) Heart(ctx, ink, new Point(0, h.Shape switch { HouseShape.Round => -96, HouseShape.Tent => -78, _ => -86 }), 8);
        if (!string.IsNullOrWhiteSpace(h.Name)) Sign(ctx, thin, h.Name, tent ? -66 : -56);
        if (deco.Contains("lamp")) Lamp(ctx, thin, time);
        if (deco.Contains("flowers")) Flowers(ctx, thin);
        if (deco.Contains("fence")) Fence(ctx, thin);
        if (home is not null) HomeTag(ctx, thin, home, h.Shape, time);
    }

    /// <summary>Snow lying along the roof (thicker as it piles up).</summary>
    private static void RoofSnow(DrawingContext ctx, HouseShape shape, double amount)
    {
        var white = new SolidColorBrush(Color.FromArgb((byte)(235 * Math.Min(1, amount + 0.3)), 0xFB, 0xFD, 0xFF));
        var thick = 3 + 7 * amount;
        var pen = new Pen(white, thick, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            switch (shape)
            {
                case HouseShape.Round:
                    gc.BeginFigure(new Point(-62, -66), false);
                    gc.ArcTo(new Point(62, -66), new Size(64, 56), 0, false, SweepDirection.Clockwise);
                    break;
                case HouseShape.Tent:
                    gc.BeginFigure(new Point(-52, -32), false);
                    gc.LineTo(new Point(0, -124));
                    gc.LineTo(new Point(52, -32));
                    break;
                default:
                    gc.BeginFigure(new Point(-70, -62), false);
                    gc.LineTo(new Point(0, -121));
                    gc.LineTo(new Point(70, -62));
                    break;
            }
        }
        ctx.DrawGeometry(null, pen, g);
    }

    /// <summary>"Momo zzz" floating over the roof while someone sleeps inside.</summary>
    private static void HomeTag(DrawingContext ctx, Pen thin, IReadOnlyList<Occupant> home, HouseShape shape, double time)
    {
        var names = string.Join(", ", home.Select(o => o.Name));
        if (names.Length > 12) names = names[..11] + "…";
        var doing = home.All(o => o.Asleep) ? L.S("is asleep", "쿨쿨") : L.S("is home", "집에 있어요");
        var bob = Math.Sin(time * 1.8) * 2;
        var top = shape switch { HouseShape.Tent => -134.0, HouseShape.Round => -124.0, _ => -120.0 };
        var ft = new FormattedText($"{names} {doing}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, SignFont, 10.5,
            new SolidColorBrush(Color.FromRgb(0x4A, 0x2E, 0x1A)));
        var w = ft.Width + 26;
        var rect = new Rect(-w / 2, top - 24 + bob, w, 17);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(235, 0xFF, 0xF6, 0xE0)), thin, rect, 8.5, 8.5);
        // Little tail pointing at the house.
        ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(235, 0xFF, 0xF6, 0xE0)), null,
            Poly(new Point(-4, rect.Bottom - 0.6), new Point(0, rect.Bottom + 5), new Point(4, rect.Bottom - 0.6)));
        ctx.DrawText(ft, new Point(rect.X + 8, rect.Y + (rect.Height - ft.Height) / 2));
        // A rising "z" at the end of the tag.
        var phase = time % 2.4 / 2.4;
        var z = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SignFont, 8 + phase * 5,
            new SolidColorBrush(Color.FromArgb((byte)(220 * (1 - phase)), 0x6A, 0x8A, 0xD8)));
        ctx.DrawText(z, new Point(rect.Right - 14 + phase * 8, rect.Y - 2 - phase * 12));
    }

    /// <summary>A sleeping face (eyes closed, ears up) in a round opening: the window or the tent door.</summary>
    private static void SleepyFace(DrawingContext ctx, Occupant o, Point c, double r, double time)
    {
        var breathe = 1 + Math.Sin(time * 2.2) * 0.03;
        r *= breathe;
        var fur = new SolidColorBrush(o.Fur);
        var line = new Pen(new SolidColorBrush(o.Dark), Math.Max(0.8, r * 0.13), lineCap: PenLineCap.Round);
        foreach (var side in new[] { -1, 1 })
        {
            var ear = o.Cat
                ? Poly(new Point(c.X + side * r * 0.95, c.Y - r * 0.25), new Point(c.X + side * r * 0.75, c.Y - r * 1.25), new Point(c.X + side * r * 0.2, c.Y - r * 0.8))
                : Poly(new Point(c.X + side * r * 1.0, c.Y - r * 0.15), new Point(c.X + side * r * 0.85, c.Y - r * 1.15), new Point(c.X + side * r * 0.25, c.Y - r * 0.85));
            ctx.DrawGeometry(fur, null, ear);
        }
        ctx.DrawEllipse(fur, null, c, r * 1.05, r);
        ctx.DrawEllipse(new SolidColorBrush(o.Belly), null, new Point(c.X, c.Y + r * 0.42), r * 0.5, r * 0.36);
        ctx.DrawEllipse(new SolidColorBrush(o.Nose), null, new Point(c.X, c.Y + r * 0.22), r * 0.14, r * 0.1);
        if (!o.Asleep)
        {
            // Awake (waiting out the rain): open eyes looking out.
            foreach (var side in new[] { -1, 1 })
            {
                var e = new Point(c.X + side * r * 0.42, c.Y - r * 0.05);
                ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x2A, 0x1C, 0x14)), null, e, r * 0.17, r * 0.19);
                ctx.DrawEllipse(Brushes.White, null, new Point(e.X - r * 0.05, e.Y - r * 0.07), r * 0.06, r * 0.06);
            }
            return;
        }
        // Closed eyes: little arcs.
        foreach (var side in new[] { -1, 1 })
        {
            var e = new Point(c.X + side * r * 0.42, c.Y - r * 0.05);
            var g = new StreamGeometry();
            using (var gc = g.Open())
            {
                gc.BeginFigure(new Point(e.X - r * 0.2, e.Y), false);
                gc.ArcTo(new Point(e.X + r * 0.2, e.Y), new Size(r * 0.2, r * 0.16), 0, false, SweepDirection.CounterClockwise);
            }
            ctx.DrawGeometry(null, line, g);
        }
    }

    // ------------------------------------------------------------------ parts

    private static void Walls(DrawingContext ctx, Pen ink, Pen thin, Color wall, bool logs)
    {
        var body = new Rect(-56, -66, 112, 66);
        ctx.DrawRectangle(new SolidColorBrush(wall), ink, body, 4, 4);
        // Shade on the right third: a hint of volume, like the pets' cel shading.
        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0x40, 0x20, 0x10)), null, new Rect(20, -64, 34, 62), 3, 3);
        if (logs)
        {
            var log = new Pen(new SolidColorBrush(Color.FromArgb(150, Ink.R, Ink.G, Ink.B)), 1.2);
            for (var y = -56; y < 0; y += 10) ctx.DrawLine(log, new Point(-54, y), new Point(54, y));
            for (var y = -61; y < 0; y += 10)
            {
                ctx.DrawEllipse(new SolidColorBrush(Mix(wall, Colors.White, 0.25)), thin, new Point(-56, y), 3.4, 4.2);
                ctx.DrawEllipse(new SolidColorBrush(Mix(wall, Colors.White, 0.25)), thin, new Point(56, y), 3.4, 4.2);
            }
        }
    }

    private static void Gable(DrawingContext ctx, Pen ink, Pen thin, Color roof)
    {
        var g = Poly(new Point(-72, -58), new Point(0, -118), new Point(72, -58), new Point(66, -52), new Point(-66, -52));
        ctx.DrawGeometry(new SolidColorBrush(roof), ink, g);
        var dark = new Pen(new SolidColorBrush(Darken(roof, 0.25)), 1.6, lineCap: PenLineCap.Round);
        for (var i = 1; i <= 3; i++)
        {
            var t = i / 4.0;
            var y = -58 - 60 * t;
            var half = 72 * (1 - t);
            ctx.DrawLine(dark, new Point(-half + 4, y + 2), new Point(half - 4, y + 2));
        }
        // Gable end under the roof.
        ctx.DrawGeometry(new SolidColorBrush(Lighten(roof, 0.35)), thin, Poly(new Point(-30, -66), new Point(0, -92), new Point(30, -66)));
    }

    private static void Dome(DrawingContext ctx, Pen ink, Pen thin, Color roof)
    {
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            gc.BeginFigure(new Point(-68, -56), true);
            gc.ArcTo(new Point(68, -56), new Size(68, 58), 0, false, SweepDirection.Clockwise);
            gc.LineTo(new Point(-68, -56));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(roof), ink, g);
        var stripe = new Pen(new SolidColorBrush(Darken(roof, 0.2)), 1.6);
        foreach (var x in new[] { -36.0, 0, 36 })
            ctx.DrawLine(stripe, new Point(x, -58), new Point(x * 0.55, -104 + Math.Abs(x) * 0.5));
        ctx.DrawEllipse(new SolidColorBrush(Lighten(roof, 0.3)), thin, new Point(0, -116), 6, 6);
    }

    private static void Tent(DrawingContext ctx, Pen ink, Pen thin, Color roof, Color wall)
    {
        ctx.DrawGeometry(new SolidColorBrush(roof), ink, Poly(new Point(-70, 0), new Point(0, -122), new Point(70, 0)));
        // Lighter canvas panel in the middle.
        ctx.DrawGeometry(new SolidColorBrush(wall), thin, Poly(new Point(-34, 0), new Point(0, -100), new Point(34, 0)));
        ctx.DrawLine(thin, new Point(0, -122), new Point(0, -132));
        ctx.DrawGeometry(new SolidColorBrush(Lighten(roof, 0.2)), thin, Poly(new Point(0, -132), new Point(14, -127), new Point(0, -123)));
    }

    private static void Window(DrawingContext ctx, Pen ink, Pen thin, bool lit, Occupant? face, double time)
    {
        var c = new Point(32, -36);
        var glass = lit ? Color.FromRgb(0xFF, 0xE2, 0x8A) : Color.FromRgb(0xBF, 0xDF, 0xF2);
        if (lit) ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, 0xFF, 0xD8, 0x6A)), null, c, 15 + (face is null ? 0 : Math.Sin(time * 2.2) * 1.2), 15);
        if (face is null)
        {
            ctx.DrawEllipse(new SolidColorBrush(glass), ink, c, 9.5, 9.5);
            ctx.DrawLine(thin, new Point(c.X - 9, c.Y), new Point(c.X + 9, c.Y));
            ctx.DrawLine(thin, new Point(c.X, c.Y - 9), new Point(c.X, c.Y + 9));
            return;
        }
        // Someone is home: a bigger window with the sleeper's face in the warm light.
        const double R = 12.5;
        ctx.DrawEllipse(new SolidColorBrush(glass), null, c, R, R);
        using (ctx.PushGeometryClip(new EllipseGeometry(new Rect(c.X - R, c.Y - R, R * 2, R * 2))))
            SleepyFace(ctx, face, new Point(c.X, c.Y + 4), 7.5, time);
        ctx.DrawEllipse(null, ink, c, R, R);
    }

    private static void Door(DrawingContext ctx, Pen ink, Pen thin, bool tent, Occupant? face, double time)
    {
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            var w = tent ? 15.0 : DoorHalfWidth;
            gc.BeginFigure(new Point(-w, 0), true);
            gc.LineTo(new Point(-w, -DoorStraight));
            gc.ArcTo(new Point(w, -DoorStraight), new Size(w, w), 0, false, SweepDirection.Clockwise);
            gc.LineTo(new Point(w, 0));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x3A, 0x26, 0x18)), ink, g);
        if (face is not null)
            using (ctx.PushGeometryClip(g))
            {
                // Warm light from inside behind the face, then the face filling the doorway.
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(120, 0xFF, 0xC8, 0x6A)), null, new Point(0, -22), 15, 20);
                SleepyFace(ctx, face, new Point(0, -17), 11.5, time);
            }
        // Doormat.
        ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xC9, 0x8E, 0x5A)), thin, new Rect(-20, -1, 40, 5), 2, 2);
    }

    private static void Sign(DrawingContext ctx, Pen thin, string name, double y)
    {
        var text = name.Length > 8 ? name[..8] : name;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, SignFont, 9.5,
            new SolidColorBrush(Color.FromRgb(0x4A, 0x2E, 0x1A)));
        var w = Math.Max(30, ft.Width + 10);
        var rect = new Rect(-w / 2, y - 7, w, 14);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF2, 0xD3, 0xA0)), thin, rect, 4, 4);
        ctx.DrawText(ft, new Point(-ft.Width / 2, y - ft.Height / 2));
    }

    private static void Chimney(DrawingContext ctx, Pen ink, Color roof, double time, HouseShape shape)
    {
        var x = shape == HouseShape.Round ? 26.0 : 34.0;
        ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xB0, 0x5A, 0x44)), ink, new Rect(x, -112, 14, 34), 2, 2);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x8E, 0x44, 0x34)), ink, new Rect(x - 2, -116, 18, 6), 2, 2);
        for (var i = 0; i < 3; i++)
        {
            var k = (time * 0.45 + i / 3.0) % 1.0;
            var r = 4 + 6 * k;
            var a = (byte)(150 * (1 - k));
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, 0xF2, 0xF2, 0xF2)), null,
                new Point(x + 7 + Math.Sin(time * 1.3 + i) * 4 + k * 10, -120 - k * 34), r, r);
        }
    }

    private static void Flags(DrawingContext ctx, Pen thin, HouseShape shape)
    {
        Color[] colors = { Color.FromRgb(0xF2, 0x6B, 0x6B), Color.FromRgb(0xFF, 0xD1, 0x5C), Color.FromRgb(0x6B, 0xC5, 0xF2), Color.FromRgb(0x8E, 0xD8, 0x7A) };
        var y0 = shape switch { HouseShape.Tent => -40.0, HouseShape.Round => -56.0, _ => -54.0 };
        var span = shape == HouseShape.Tent ? 46.0 : 64.0;
        const int n = 8;
        var line = new Pen(new SolidColorBrush(Color.FromRgb(0x7A, 0x5A, 0x3A)), 1);
        var prev = new Point(-span, y0);
        for (var i = 0; i < n; i++)
        {
            var x0 = -span + 2 * span * i / n;
            var x1 = -span + 2 * span * (i + 1) / n;
            double Sag(double x) => y0 + 7 * (1 - Math.Pow(x / span, 2));
            var a = new Point(x0, Sag(x0));
            var b = new Point(x1, Sag(x1));
            ctx.DrawLine(line, prev, b);
            prev = b;
            ctx.DrawGeometry(new SolidColorBrush(colors[i % colors.Length]), thin,
                Poly(a, b, new Point((x0 + x1) / 2, (a.Y + b.Y) / 2 + 10)));
        }
    }

    private static void Heart(DrawingContext ctx, Pen ink, Point c, double r)
    {
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            gc.BeginFigure(new Point(c.X, c.Y + r * 1.1), true);
            gc.CubicBezierTo(new Point(c.X - r * 1.6, c.Y), new Point(c.X - r * 1.0, c.Y - r * 1.3), new Point(c.X, c.Y - r * 0.45));
            gc.CubicBezierTo(new Point(c.X + r * 1.0, c.Y - r * 1.3), new Point(c.X + r * 1.6, c.Y), new Point(c.X, c.Y + r * 1.1));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xF0, 0x5A, 0x7E)), ink, g);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), null, new Point(c.X - r * 0.45, c.Y - r * 0.35), r * 0.22, r * 0.16);
    }

    private static void Lamp(DrawingContext ctx, Pen thin, double time)
    {
        var c = new Point(-27, -34);
        var flicker = 0.85 + 0.15 * Math.Sin(time * 7) * Math.Sin(time * 3.1);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(90 * flicker), 0xFF, 0xD8, 0x6A)), null, c, 14, 14);
        ctx.DrawLine(thin, new Point(c.X, c.Y - 14), new Point(c.X, c.Y - 7));
        ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xFF, 0xE3, 0x8C)), thin, new Rect(c.X - 4.5, c.Y - 7, 9, 12), 2, 2);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x4A, 0x3A, 0x30)), thin, new Rect(c.X - 5.5, c.Y - 9, 11, 3), 1, 1);
    }

    private static void Flowers(DrawingContext ctx, Pen thin)
    {
        Color[] petals = { Color.FromRgb(0xF2, 0x6B, 0x8E), Color.FromRgb(0xFF, 0xD1, 0x5C), Color.FromRgb(0xB0, 0x8A, 0xF0) };
        foreach (var x in new[] { -30.0, 30 })
        {
            ctx.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xC8, 0x6A, 0x3E)), thin,
                Poly(new Point(x - 7, -11), new Point(x + 7, -11), new Point(x + 5, 0), new Point(x - 5, 0)));
            for (var i = 0; i < 3; i++)
            {
                var fx = x - 5 + i * 5;
                var fy = -16 - (i == 1 ? 5 : 0);
                ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x4F, 0x9A, 0x4A)), 1.2), new Point(fx, -11), new Point(fx, fy));
                ctx.DrawEllipse(new SolidColorBrush(petals[(i + (x > 0 ? 1 : 0)) % petals.Length]), thin, new Point(fx, fy), 3.2, 3.2);
            }
        }
    }

    private static void Fence(DrawingContext ctx, Pen thin)
    {
        var wood = new SolidColorBrush(Color.FromRgb(0xF4, 0xEB, 0xDA));
        foreach (var side in new[] { -1, 1 })
        {
            var x0 = side * 50.0;
            var x1 = side * 82.0;
            ctx.DrawRectangle(wood, thin, new Rect(Math.Min(x0, x1), -12, Math.Abs(x1 - x0), 4), 1, 1);
            for (var i = 0; i < 4; i++)
            {
                var x = x0 + (x1 - x0) * i / 3.0;
                ctx.DrawGeometry(wood, thin, Poly(new Point(x - 3, 4), new Point(x - 3, -16), new Point(x, -20), new Point(x + 3, -16), new Point(x + 3, 4)));
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private static StreamGeometry Poly(params Point[] pts)
    {
        var g = new StreamGeometry();
        using var gc = g.Open();
        gc.BeginFigure(pts[0], true);
        for (var i = 1; i < pts.Length; i++) gc.LineTo(pts[i]);
        gc.EndFigure(true);
        return g;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static Color Darken(Color c, double t) => Mix(c, Colors.Black, t);
    private static Color Lighten(Color c, double t) => Mix(c, Colors.White, t);
}

/// <summary>A built house on the backyard canvas. Its feet line is the house base.</summary>
public sealed class HouseSprite : Control
{
    public PetHouse House { get; }
    public double Scale { get; set; } = 1.0;
    public bool Occupied { get; set; }

    /// <summary>Who is sleeping inside.</summary>
    public IReadOnlyList<HouseArt.Occupant>? Occupants { get; set; }

    /// <summary>0..1 snow on the roof.</summary>
    public double Snow { get; set; }
    private float _depth = 1f;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    public HouseSprite(PetHouse house)
    {
        House = house;
        IsHitTestVisible = false;
    }

    public void Place(Cell cell)
    {
        _depth = cell.Map.DepthScale(House.Position.Y);
        Width = HouseArt.BoxWidth * _depth / Scale;
        Height = HouseArt.BoxHeight * _depth / Scale;
        Canvas.SetLeft(this, (House.Position.X - HouseArt.BoxWidth / 2f * _depth - cell.Bounds.X) / Scale);
        Canvas.SetTop(this, (House.Position.Y - HouseArt.BaseY * _depth - cell.Bounds.Y) / Scale);
        ZIndex = (int)House.Position.Y;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var _ = context.PushTransform(Matrix.CreateScale(_depth / Scale, _depth / Scale));
        HouseArt.Draw(context, House, new Point(HouseArt.BoxWidth / 2.0, HouseArt.BaseY), 1, Clock.Elapsed.TotalSeconds, Occupied, Occupants, Snow);
    }
}

/// <summary>A house drawn at a fixed size, for the decorating window.</summary>
public sealed class HousePreview : Control
{
    public PetHouse? House { get; set; }
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var b = Bounds;
        context.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xDF, 0xF0, 0xFB), 0),
                new GradientStop(Color.FromRgb(0xE9, 0xF6, 0xEA), 0.62),
                new GradientStop(Color.FromRgb(0xB8, 0xDF, 0xA6), 0.63),
                new GradientStop(Color.FromRgb(0x9C, 0xCF, 0x8A), 1),
            },
        }, null, new Rect(b.Size), 14, 14);
        if (House is null) return;
        var scale = Math.Min(b.Width / 200, b.Height / 170);
        HouseArt.Draw(context, House, new Point(b.Width / 2, b.Height * 0.82), scale, Clock.Elapsed.TotalSeconds, false);
    }
}
