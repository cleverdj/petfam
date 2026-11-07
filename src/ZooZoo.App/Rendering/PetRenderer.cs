using System.Globalization;
using Avalonia;
using Avalonia.Media;
using ZooZoo.Core.Geometry;
using ZooZoo.Core.Pets;

namespace ZooZoo.Desktop.Rendering;

/// <summary>
/// Pseudo-3D quadruped: the rig lives in 3D model space (x forward, y up, z to the animal's right),
/// is rotated by the pet's heading, projected with a slightly elevated camera, depth-sorted and drawn
/// as shaded tubes and spheres with a contact shadow. No mesh assets: every part is a capsule or a
/// sphere, which is what the placeholder-art weeks of the plan (§9.8) call for.
/// </summary>
public static class PetRenderer
{
    // ------------------------------------------------------------------ dimensions & style

    public sealed record Dims(float BodyLen, float BodyR, float ShoulderH, float HipH, float L1, float L2, float HeadR, float Neck, float Muzzle);

    /// <summary>Cat = Persian (stocky, flat face, long coat). Dog = Pomeranian (small, round, very fluffy).</summary>
    public static Dims DimsOf(PetSpecies s) => s switch
    {
        PetSpecies.Cat => new Dims(40, 13, 22, 23, 11, 10, 19, 5, 2),
        _ => new Dims(38, 14, 21, 21, 10, 9, 19, 5, 4),
    };

    /// <summary>Fur displacement amplitude (model units) used by the 3D shader.</summary>
    public static float FurAmount(PetSpecies s) => s == PetSpecies.Cat ? 1.1f : 1.5f;

    /// <summary>
    /// Cartoon look (default): cel shading in flat bands, brown outlines, big glossy cartoon eyes, no fur noise.
    /// ZOOZOO_STYLE=fur switches back to the soft fur look.
    /// </summary>
    public static bool Toon { get; } = Environment.GetEnvironmentVariable("ZOOZOO_STYLE") != "fur";

    /// <summary>Outline colour of the cartoon style.</summary>
    public static (float R, float G, float B) LineColor(PetSpecies s) => s == PetSpecies.Cat
        ? (0x86 / 255f, 0x48 / 255f, 0x1E / 255f)
        : (0x6A / 255f, 0x38 / 255f, 0x14 / 255f);

    private sealed record Style(Color Fur, Color Belly, Color Dark, Color Nose, Color EarInner, Color Paw,
        Color Saddle, Color Chest, Color Leg, Color Iris);

    private enum Eye { Open, Closed, Happy, Wide }

    private const double CameraPitchDeg = 24;
    private static readonly double SinP = Math.Sin(CameraPitchDeg * Math.PI / 180);
    private static readonly double CosP = Math.Cos(CameraPitchDeg * Math.PI / 180);

    public const float PeekHeadRadius = 26f;

    // ------------------------------------------------------------------ 3D math

    private readonly record struct V3(double X, double Y, double Z)
    {
        public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public V3 Normalized => Length < 1e-6 ? this : this * (1 / Length);
        public static V3 Lerp(V3 a, V3 b, double t) => a + (b - a) * t;
    }

    /// <summary>Rotates model space by the body yaw and projects to screen pixels relative to the feet origin.</summary>
    private sealed class Camera
    {
        private readonly double _cos, _sin;

        public Camera(double yawDeg)
        {
            var r = yawDeg * Math.PI / 180;
            _cos = Math.Cos(r);
            _sin = Math.Sin(r);
        }

        /// <summary>World-space (screen-aligned) coordinates: xr right, y up, zr toward the viewer.</summary>
        public (double xr, double y, double zr) World(V3 p) => (p.X * _cos - p.Z * _sin, p.Y, p.X * _sin + p.Z * _cos);

        public Point Project(V3 p)
        {
            var (xr, y, zr) = World(p);
            return new Point(xr, -y * CosP + zr * SinP);
        }

        public double Depth(V3 p) => World(p).zr + p.Y * 0.05;

        /// <summary>Scale factor for a part at that depth (subtle perspective).</summary>
        public double Scale(V3 p) => 1 + World(p).zr * 0.0022;

        /// <summary>Convert a screen-plane direction (dx right, dy down) into a model-space ground direction.</summary>
        public V3 ScreenDirToModel(double dx, double dy)
        {
            var xr = dx;
            var zr = dy;   // down the screen = toward the viewer
            return new V3(xr * _cos + zr * _sin, 0, -xr * _sin + zr * _cos);
        }
    }

    // ------------------------------------------------------------------ scene (depth-sorted primitives)

    private abstract class Prim
    {
        public double Depth;
        public abstract void Draw(DrawingContext ctx, Point origin);
    }

    private sealed class SpherePrim : Prim
    {
        public Point C; public double R; public Color Color; public bool Edge = true;
        public override void Draw(DrawingContext ctx, Point o) => DrawSphere(ctx, new Point(o.X + C.X, o.Y + C.Y), R, Color, Edge);
    }

    private sealed class CapsulePrim : Prim
    {
        public Point A, B; public double R; public Color Color;
        public override void Draw(DrawingContext ctx, Point o) =>
            DrawCapsule(ctx, new Point(o.X + A.X, o.Y + A.Y), new Point(o.X + B.X, o.Y + B.Y), R, Color);
    }

    private sealed class TriPrim : Prim
    {
        public Point A, B, C; public Color Color; public Color? Inner;
        public override void Draw(DrawingContext ctx, Point o)
        {
            var g = new StreamGeometry();
            using (var gc = g.Open())
            {
                gc.BeginFigure(new Point(o.X + A.X, o.Y + A.Y), true);
                gc.LineTo(new Point(o.X + B.X, o.Y + B.Y));
                gc.LineTo(new Point(o.X + C.X, o.Y + C.Y));
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(Gradient(Color), EdgePen(Color), g);
            if (Inner is { } inner)
            {
                var gi = new StreamGeometry();
                using (var gc = gi.Open())
                {
                    var mid = new Point(o.X + (A.X + B.X + C.X) / 3, o.Y + (A.Y + B.Y + C.Y) / 3);
                    Point Shrink(Point p) => new(mid.X + (o.X + p.X - mid.X) * 0.55, mid.Y + (o.Y + p.Y - mid.Y) * 0.55);
                    gc.BeginFigure(Shrink(A), true);
                    gc.LineTo(Shrink(B));
                    gc.LineTo(Shrink(C));
                    gc.EndFigure(true);
                }
                ctx.DrawGeometry(new SolidColorBrush(inner), null, gi);
            }
        }
    }

    private sealed class EyePrim : Prim
    {
        public Point C; public double R; public Eye State; public Point Pupil; public Point A, B;
        public override void Draw(DrawingContext ctx, Point o)
        {
            var c = new Point(o.X + C.X, o.Y + C.Y);
            switch (State)
            {
                case Eye.Closed:
                    ctx.DrawLine(new Pen(Brushes.Black, 1.6) { LineCap = PenLineCap.Round }, new Point(o.X + A.X, o.Y + A.Y), new Point(o.X + B.X, o.Y + B.Y));
                    break;
                case Eye.Happy:
                {
                    var g = new StreamGeometry();
                    using (var gc = g.Open())
                    {
                        gc.BeginFigure(new Point(o.X + A.X, o.Y + A.Y), false);
                        gc.QuadraticBezierTo(new Point(c.X, c.Y - R * 1.3), new Point(o.X + B.X, o.Y + B.Y));
                        gc.EndFigure(false);
                    }
                    ctx.DrawGeometry(null, new Pen(Brushes.Black, 1.8) { LineCap = PenLineCap.Round }, g);
                    break;
                }
                default:
                {
                    var wide = State == Eye.Wide;
                    var r = R * (wide ? 1.2 : 1);
                    ctx.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), 0.8), c, r, r);
                    var p = new Point(o.X + Pupil.X, o.Y + Pupil.Y);
                    ctx.DrawEllipse(Brushes.Black, null, p, r * (wide ? 0.42 : 0.58), r * (wide ? 0.42 : 0.58));
                    ctx.DrawEllipse(Brushes.White, null, new Point(p.X - r * 0.2, p.Y - r * 0.25), r * 0.16, r * 0.16);
                    break;
                }
            }
        }
    }

    /// <summary>One SDF primitive for the GPU renderer: a round cone in world space (x right, y up, z toward viewer).</summary>
    public readonly record struct SdfPrim(float Ax, float Ay, float Az, float R1, float Bx, float By, float Bz, float R2,
        float Cr, float Cg, float Cb, float K, float Gloss, float Aux1 = 0, float Aux2 = 0, float Aux3 = 0);

    private sealed class Scene
    {
        public readonly List<Prim> Prims = new();
        public readonly List<SdfPrim> Sdf = new();
        public readonly List<(V3 A, V3 B)> Whiskers = new();
        public V3 HeadCenter;
        public readonly Camera Cam;
        public Scene(Camera cam) => Cam = cam;

        /// <summary>Smooth-union radius for the next SDF primitives (0 = hard union, drawn on top).</summary>
        public double K = 3;
        public double Gloss = 0.04;

        /// <summary>Tabby stripe strength for the next primitives (0 = plain).</summary>
        public double Stripe;

        /// <summary>
        /// Adds a round cone. Aux carries the stripe strength, or for eyes (gloss = -1) the eye's forward direction,
        /// which the shader uses to draw iris, pupil and catchlights.
        /// </summary>
        public void AddSdf(V3 a, V3 b, double r1, double r2, Color c, double? k = null, double? gloss = null, V3? eyeDir = null)
        {
            var wa = Cam.World(a);
            var wb = Cam.World(b);
            float g = (float)(gloss ?? Gloss), a1 = (float)Stripe, a2 = 0, a3 = 0;
            if (eyeDir is { } e)
            {
                var we = Cam.World(e.Normalized);
                g = -1f; a1 = (float)we.xr; a2 = (float)we.y; a3 = (float)we.zr;
            }
            Sdf.Add(new SdfPrim((float)wa.xr, (float)wa.y, (float)wa.zr, (float)r1,
                (float)wb.xr, (float)wb.y, (float)wb.zr, (float)r2,
                c.R / 255f, c.G / 255f, c.B / 255f, (float)(k ?? K), g, a1, a2, a3));
        }

        public void Sphere(V3 c, double r, Color color, bool edge = true, double? k = null, double? gloss = null)
        {
            Prims.Add(new SpherePrim { C = Cam.Project(c), R = r * Cam.Scale(c), Color = color, Edge = edge, Depth = Cam.Depth(c) });
            AddSdf(c, c, r, r, color, k, gloss);
        }

        public void Capsule(V3 a, V3 b, double r, Color color, double? k = null)
        {
            var mid = V3.Lerp(a, b, 0.5);
            Prims.Add(new CapsulePrim { A = Cam.Project(a), B = Cam.Project(b), R = r * Cam.Scale(mid), Color = color, Depth = Cam.Depth(mid) });
            AddSdf(a, b, r, r, color, k);
        }

        /// <summary>A bent tube: several capsules along the points (tapered round cones in 3D).</summary>
        public void Chain(IReadOnlyList<V3> pts, double r0, double r1, Color color)
        {
            for (var i = 0; i < pts.Count - 1; i++)
            {
                var t = (i + 0.5) / (pts.Count - 1);
                var mid = V3.Lerp(pts[i], pts[i + 1], 0.5);
                Prims.Add(new CapsulePrim
                {
                    A = Cam.Project(pts[i]), B = Cam.Project(pts[i + 1]),
                    R = (r0 + (r1 - r0) * t) * Cam.Scale(mid), Color = color, Depth = Cam.Depth(mid),
                });
                var ra = r0 + (r1 - r0) * i / (pts.Count - 1.0);
                var rb = r0 + (r1 - r0) * (i + 1) / (pts.Count - 1.0);
                AddSdf(pts[i], pts[i + 1], ra, rb, color);
            }
        }

        /// <summary>Ear: triangle (a, tip b, c) in 2D; a cone from the base to the tip in 3D, plus an inner cone.</summary>
        public void Tri(V3 a, V3 b, V3 c, Color color, Color? inner = null, V3? innerOffset = null, double tipRound = 0.16)
        {
            var mid = (a + b + c) * (1.0 / 3);
            Prims.Add(new TriPrim { A = Cam.Project(a), B = Cam.Project(b), C = Cam.Project(c), Color = color, Inner = inner, Depth = Cam.Depth(mid) });

            var baseC = V3.Lerp(a, c, 0.5);
            var baseR = (a - c).Length * 0.42;
            AddSdf(baseC, b, baseR, baseR * tipRound, color);
            if (inner is { } ic && innerOffset is { } off)
                AddSdf(baseC + off, V3.Lerp(baseC, b, 0.78) + off, baseR * 0.62, baseR * 0.14, ic, k: 0.0, gloss: 0.0);
        }

        public void EyeAt(V3 c, double r, Eye state, V3 pupilDir, V3 lidA, V3 lidB)
        {
            var pc = Cam.Project(c);
            var pupil = Cam.Project(c + pupilDir * (r * 0.4));
            Prims.Add(new EyePrim { C = pc, R = r, State = state, Pupil = pupil, A = Cam.Project(lidA), B = Cam.Project(lidB), Depth = Cam.Depth(c) + 0.3 });
        }

        public void Flush(DrawingContext ctx, Point origin)
        {
            Prims.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            foreach (var p in Prims) p.Draw(ctx, origin);
        }
    }

    // ------------------------------------------------------------------ shaded primitives

    private static readonly Dictionary<uint, RadialGradientBrush> GradientCache = new();
    private static readonly Dictionary<uint, Pen> EdgeCache = new();

    private static RadialGradientBrush Gradient(Color c)
    {
        var key = c.ToUInt32();
        if (GradientCache.TryGetValue(key, out var b)) return b;
        b = new RadialGradientBrush
        {
            Center = new RelativePoint(0.36, 0.32, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.36, 0.32, RelativeUnit.Relative),
            Radius = 0.78,
            GradientStops =
            {
                new GradientStop(Lighten(c, 0.42), 0),
                new GradientStop(c, 0.5),
                new GradientStop(Darken(c, 0.42), 1),
            },
        };
        GradientCache[key] = b;
        return b;
    }

    private static Pen EdgePen(Color c)
    {
        var key = c.ToUInt32();
        if (EdgeCache.TryGetValue(key, out var p)) return p;
        var d = Darken(c, 0.55);
        p = new Pen(new SolidColorBrush(Color.FromArgb(150, d.R, d.G, d.B)), 1.2) { LineJoin = PenLineJoin.Round };
        EdgeCache[key] = p;
        return p;
    }

    private static void DrawSphere(DrawingContext ctx, Point c, double r, Color color, bool edge)
    {
        if (edge)
            ctx.DrawEllipse(null, EdgePen(color), c, r + 0.4, r + 0.4);
        ctx.DrawEllipse(Gradient(color), null, c, r, r);
    }

    private sealed record CapsulePens(Pen Edge, Pen Body, Pen Shadow, Pen Highlight);
    private static readonly Dictionary<(uint, int), CapsulePens> CapsuleCache = new();

    private static CapsulePens PensFor(Color color, double r)
    {
        var key = (color.ToUInt32(), (int)Math.Round(r * 4));
        if (CapsuleCache.TryGetValue(key, out var pens)) return pens;
        r = key.Item2 / 4.0;
        var dark = Darken(color, 0.55);
        var shade = Darken(color, 0.38);
        var light = Lighten(color, 0.4);
        pens = new CapsulePens(
            new Pen(new SolidColorBrush(Color.FromArgb(150, dark.R, dark.G, dark.B)), r * 2 + 2.4) { LineCap = PenLineCap.Round },
            new Pen(new SolidColorBrush(color), r * 2) { LineCap = PenLineCap.Round },
            new Pen(new SolidColorBrush(Color.FromArgb(110, shade.R, shade.G, shade.B)), r * 1.1) { LineCap = PenLineCap.Round },
            new Pen(new SolidColorBrush(Color.FromArgb(170, light.R, light.G, light.B)), r * 0.8) { LineCap = PenLineCap.Round });
        CapsuleCache[key] = pens;
        return pens;
    }

    private static void DrawCapsule(DrawingContext ctx, Point a, Point b, double r, Color color)
    {
        var pens = PensFor(color, r);
        ctx.DrawLine(pens.Edge, a, b);
        ctx.DrawLine(pens.Body, a, b);
        var so = new Vector(r * 0.3, r * 0.36);
        ctx.DrawLine(pens.Shadow, a + so, b + so);
        var ho = new Vector(-r * 0.28, -r * 0.34);
        ctx.DrawLine(pens.Highlight, a + ho, b + ho);
    }

    private static void DrawShadow(DrawingContext ctx, Point c, double rx, double ry, double alpha)
    {
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Radius = 0.5,
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(alpha * 255), 0, 0, 0), 0),
                new GradientStop(Color.FromArgb((byte)(alpha * 120), 0, 0, 0), 0.6),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1),
            },
        };
        ctx.DrawEllipse(brush, null, c, rx, ry);
    }

    // ------------------------------------------------------------------ public API

    public static void DrawBody(DrawingContext ctx, Pet pet, Vec2 origin, float depth)
    {
        var d = DimsOf(pet.Species);
        var style = StyleOf(pet);
        var t = pet.AnimationTime;
        var cam = new Camera(pet.Heading);
        var scene = new Scene(cam);
        var o = new Point(origin.X, origin.Y);

        // Look target in screen pixels relative to the feet (depth 1).
        Vec2? look = pet.LookAt is { } la ? new Vec2((la.X - pet.Position.X) / depth, (la.Y - pet.Position.Y) / depth) : null;

        var pose = BuildPose(pet, d, t);
        ApplyLook(pose, cam, d, look, pet);

        // Contact shadow first.
        var yaw = pet.Heading * Math.PI / 180;
        var lifted = pet.Animation == "carried";
        var shadowRx = d.BodyLen * 0.42 * Math.Abs(Math.Cos(yaw)) + d.BodyR * 1.3;
        var shadowRy = (d.BodyLen * 0.42 * Math.Abs(Math.Sin(yaw)) * SinP) + d.BodyR * 0.55;
        if (lifted)
        {
            shadowRx *= 0.45;
            shadowRy *= 0.45;
        }
        DrawShadow(ctx, new Point(o.X, o.Y + 2), shadowRx, shadowRy, lifted ? 0.16 : 0.32);

        BuildScene(scene, pet, d, style, pose, t);
        scene.Flush(ctx, o);

        DrawOverlays(ctx, pet, origin);
    }

    /// <summary>
    /// Builds the SDF primitive list for the GPU renderer (world space, feet at the origin, depth-1 units).
    /// Smooth primitives come first, hard ones (eyes, nose) last, as the shader expects.
    /// </summary>
    /// <summary>SDF primitives plus projected whisker lines (x1,y1,x2,y2,alpha relative to the feet).</summary>
    public sealed record SdfBuild(List<SdfPrim> Prims, List<float> Whiskers, float Fur, float WhiskerWidth);

    public static SdfBuild BuildSdf(Pet pet, float depth)
    {
        var d = DimsOf(pet.Species);
        var style = StyleOf(pet);
        var cam = new Camera(pet.Heading);
        var scene = new Scene(cam);
        Vec2? look = pet.LookAt is { } la ? new Vec2((la.X - pet.Position.X) / depth, (la.Y - pet.Position.Y) / depth) : null;
        var pose = BuildPose(pet, d, pet.AnimationTime);
        ApplyLook(pose, cam, d, look, pet);
        BuildScene(scene, pet, d, style, pose, pet.AnimationTime);

        var list = new List<SdfPrim>(scene.Sdf.Count);
        list.AddRange(scene.Sdf.Where(p => p.K > 0));
        list.AddRange(scene.Sdf.Where(p => p.K <= 0));

        // Whiskers: hidden when the muzzle points away, faint on the far side of the face.
        var whiskers = new List<float>();
        var headZ = cam.World(scene.HeadCenter).zr;
        foreach (var (a, b) in scene.Whiskers)
        {
            var za = cam.World(a).zr;
            if (za < headZ - d.HeadR * 0.6) continue;
            var alpha = za < headZ - d.HeadR * 0.1 ? 0.35f : 0.9f;
            var pa = cam.Project(a);
            var pb = cam.Project(b);
            whiskers.AddRange(new[] { (float)pa.X, (float)pa.Y, (float)pb.X, (float)pb.Y, alpha });
        }

        return new SdfBuild(list, whiskers, Toon ? 0f : FurAmount(pet.Species), pet.Species == PetSpecies.Cat ? 0.55f : 0.45f);
    }

    public static float CameraSin => (float)SinP;
    public static float CameraCos => (float)CosP;

    /// <summary>2D effects drawn over the body: water drops, dirt, hearts, "z z".</summary>
    public static void DrawOverlays(DrawingContext ctx, Pet pet, Vec2 origin)
    {
        var d = DimsOf(pet.Species);
        var t = pet.AnimationTime;
        var o = new Point(origin.X, origin.Y);
        DrawParticles(ctx, pet, d, o, t, new Camera(pet.Heading));

        if (pet.Animation == "sleep")
        {
            var text = new FormattedText("z z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface.Default, 13, new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)));
            var rise = (t % 2.2f) * 9f;
            ctx.DrawText(text, new Point(o.X + 14, o.Y - d.BodyR * 3 - 14 - rise));
        }

        // Head top in sprite units, used to stack the plumbob and the speech bubble above the pet.
        var headTop = o.Y - (d.ShoulderH + d.Neck + d.HeadR * 2.9) * CosP;
        if (pet.Animation is "sleep" or "lie") headTop = o.Y - (d.HeadR * 2.6) * CosP;

        if (pet.Selected) DrawPlumbob(ctx, new Point(o.X, headTop - 16), MoodCalc.Level(pet));
        if (pet.Bubble is { } bubble) DrawBubble(ctx, new Point(o.X, headTop - (pet.Selected ? 40 : 10)), bubble, pet.BubbleTimer);
    }

    /// <summary>The Sims plumbob: a spinning green (yellow, red) diamond floating above the selected pet.</summary>
    private static void DrawPlumbob(DrawingContext ctx, Point bottom, MoodLevel mood)
    {
        var t = DateTime.UtcNow.TimeOfDay.TotalSeconds;
        var bob = Math.Sin(t * 2.2) * 2.0;
        var spin = Math.Cos(t * 1.6);
        var w = 7.5 * Math.Max(0.25, Math.Abs(spin));
        const double h = 13;
        var c = new Point(bottom.X, bottom.Y - h + bob);

        var (light, mid, dark) = mood switch
        {
            MoodLevel.Good => (Color.FromRgb(0xB8, 0xFF, 0xA8), Color.FromRgb(0x3C, 0xD0, 0x5A), Color.FromRgb(0x1C, 0x7A, 0x30)),
            MoodLevel.Okay => (Color.FromRgb(0xFF, 0xF0, 0xA0), Color.FromRgb(0xF0, 0xC8, 0x3C), Color.FromRgb(0x96, 0x74, 0x10)),
            _ => (Color.FromRgb(0xFF, 0xB0, 0xA0), Color.FromRgb(0xE0, 0x4A, 0x3C), Color.FromRgb(0x84, 0x20, 0x18)),
        };

        var top = new Point(c.X, c.Y - h);
        var bot = new Point(c.X, c.Y + h);
        var left = new Point(c.X - w, c.Y - 2);
        var right = new Point(c.X + w, c.Y - 2);

        void Facet(Point a, Point b, Point d, Color col)
        {
            var g = new StreamGeometry();
            using (var gc = g.Open()) { gc.BeginFigure(a, true); gc.LineTo(b); gc.LineTo(d); gc.EndFigure(true); }
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(235, col.R, col.G, col.B)), null, g);
        }

        var front = spin >= 0;
        Facet(top, left, c, front ? light : mid);
        Facet(top, c, right, front ? mid : light);
        Facet(bot, left, c, front ? mid : dark);
        Facet(bot, c, right, front ? dark : mid);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, mid.R, mid.G, mid.B)), null, new Point(c.X, bottom.Y + 6), 6, 2);
    }

    private static readonly Typeface BubbleFont = new("Malgun Gothic");

    private static void DrawBubble(DrawingContext ctx, Point tip, string text, float timeLeft)
    {
        var alpha = (byte)(255 * Math.Clamp(timeLeft / 0.3, 0, 1));
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, BubbleFont, 13,
            new SolidColorBrush(Color.FromArgb(alpha, 0x1C, 0x2A, 0x40)));
        var w = ft.Width + 16;
        var h = ft.Height + 8;
        var rect = new Rect(tip.X - w / 2, tip.Y - h - 7, w, h);
        var fill = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.95), 255, 255, 255));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.6), 0x2F, 0x6F, 0xD6)), 1.2);
        ctx.DrawRectangle(fill, pen, rect, 8, 8);
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            gc.BeginFigure(new Point(tip.X - 5, rect.Bottom - 0.5), true);
            gc.LineTo(new Point(tip.X, tip.Y));
            gc.LineTo(new Point(tip.X + 5, rect.Bottom - 0.5));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(fill, null, g);
        ctx.DrawText(ft, new Point(rect.X + 8, rect.Y + 4));
    }

    private static V3 Cross(V3 a, V3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static RectI HitBox(Pet pet, float depth)
    {
        var d = DimsOf(pet.Species);
        var half = d.BodyLen * 0.5f + d.Neck + d.HeadR * 2f + d.Muzzle;
        var standTop = d.ShoulderH + d.Neck + d.HeadR * 2.2f;
        var carriedTop = (d.L1 + d.L2) * 0.85f + d.BodyLen * 0.85f + d.Neck + d.HeadR * 1.8f;
        var top = ((pet.Animation == "carried" ? carriedTop : standTop) + 6f) * (float)CosP;
        return RectI.FromLTRB(
            (int)(pet.Position.X - half * depth), (int)(pet.Position.Y - top * depth),
            (int)(pet.Position.X + half * depth), (int)(pet.Position.Y + 10f * depth));
    }

    /// <summary>Feet → scruff offset in screen pixels (depth 1) for the carried pose.</summary>
    public static Vec2 GrabOffset(PetSpecies species)
    {
        var d = DimsOf(species);
        var y = (d.L1 + d.L2) * 0.85 + d.BodyLen * 0.85 + d.BodyR * 0.9;
        return new Vec2(0f, (float)(-y * CosP));
    }

    public static (Point Center, Vector Outward) PeekHeadPose(PeekState s)
    {
        var r = s.WindowRect;
        const float R = PeekHeadRadius;
        var travel = R * 1.6f * s.Progress;
        return s.Edge switch
        {
            PeekEdge.Top => (new Point(s.Anchor.X, r.Top + R - travel), new Vector(0, -1)),
            PeekEdge.Bottom => (new Point(s.Anchor.X, r.Bottom - R + travel), new Vector(0, 1)),
            PeekEdge.Left => (new Point(r.Left + R - travel, s.Anchor.Y), new Vector(-1, 0)),
            _ => (new Point(r.Right - R + travel, s.Anchor.Y), new Vector(1, 0)),
        };
    }

    /// <summary>Head poking out from a window edge (2D, absolute pixels), shaded like the 3D parts.</summary>
    public static void DrawPeek(DrawingContext ctx, Pet pet, Vec2 cursor)
    {
        var s = pet.Peek;
        if (s is null) return;

        var r = s.WindowRect;
        var outer = new RectangleGeometry(new Rect(-100000, -100000, 200000, 200000));
        var inner = new RectangleGeometry(new Rect(r.X, r.Y, r.Width, r.Height));
        var clip = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);

        var style = StyleOf(pet);
        var (center, outward) = PeekHeadPose(s);
        var perp = new Vector(-outward.Y, outward.X);
        const double R = PeekHeadRadius;

        using var _ = ctx.PushGeometryClip(clip);

        // Ears (both breeds have upright ears).
        {
            foreach (var sign in new[] { -1, 1 })
            {
                var b1 = center + outward * (R * 0.5) + perp * (sign * R * 0.85);
                var b2 = center + outward * (R * 0.85) + perp * (sign * R * 0.3);
                var tip = center + outward * (R * 1.5) + perp * (sign * R * 0.7);
                var g = new StreamGeometry();
                using (var gc = g.Open()) { gc.BeginFigure(b1, true); gc.LineTo(tip); gc.LineTo(b2); gc.EndFigure(true); }
                ctx.DrawGeometry(Gradient(style.Fur), EdgePen(style.Fur), g);
            }
        }

        DrawSphere(ctx, center, R, style.Fur, true);
        DrawSphere(ctx, center + outward * (R * 0.45), R * 0.5, style.Belly, false);
        ctx.DrawEllipse(new SolidColorBrush(style.Nose), null, center + outward * (R * 0.8), 2.6, 2.1);

        if (s.Edge is PeekEdge.Top or PeekEdge.Bottom)
        {
            var pawY = s.Edge == PeekEdge.Top ? r.Top - 3 : r.Bottom + 3;
            DrawSphere(ctx, new Point(center.X - 14, pawY), 6, style.Fur, true);
            DrawSphere(ctx, new Point(center.X + 14, pawY), 6, style.Fur, true);
        }

        var eyeOffset = outward * 2;
        DrawTrackingEye(ctx, center + perp * 6.5 + eyeOffset, cursor);
        DrawTrackingEye(ctx, center - perp * 6.5 + eyeOffset, cursor);
    }

    private static void DrawTrackingEye(DrawingContext ctx, Point eye, Vec2 cursor)
    {
        ctx.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), 0.8), eye, 4, 4);
        var dx = cursor.X - eye.X;
        var dy = cursor.Y - eye.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        var px = len < 1 ? 0 : dx / len * 2.0;
        var py = len < 1 ? 0 : dy / len * 2.0;
        ctx.DrawEllipse(Brushes.Black, null, new Point(eye.X + px, eye.Y + py), 2.1, 2.1);
        ctx.DrawEllipse(Brushes.White, null, new Point(eye.X + px - 0.7, eye.Y + py - 0.8), 0.7, 0.7);
    }

    // ------------------------------------------------------------------ pose (3D)

    private sealed class Pose
    {
        public V3 Shoulder, Hip;                 // trunk end points (centre line)
        public V3 Head;                          // head centre
        public double HeadYaw, HeadPitch;        // degrees, relative to the body; pitch + = nose down
        public V3?[] Feet = new V3?[4];          // 0 RF, 1 LF, 2 RH, 3 LH  (R = +z)
        public V3?[] Knees = new V3?[4];         // explicit knees for folded legs; null → IK
        public bool HindFolded;                  // sitting / lying: hind legs tucked
        public bool TailOnGround;
        public double TailWag = 1, TailLift;
        public Eye Eyes = Eye.Open;
        public double Mouth;
        public bool Tongue;
        public double EarBack;
        public bool EarFlap;
        public bool CurledSleep, SideSleep;
    }

    private static Pose Standing(Dims d, double bob = 0)
    {
        var p = new Pose
        {
            Shoulder = new V3(d.BodyLen / 2 - 5, d.ShoulderH + bob, 0),
            Hip = new V3(-d.BodyLen / 2 + 5, d.HipH + bob, 0),
        };
        var zs = d.BodyR * 0.55;
        var zh = d.BodyR * 0.5;
        p.Feet[0] = new V3(p.Shoulder.X + 2, 0, zs);
        p.Feet[1] = new V3(p.Shoulder.X - 1, 0, -zs);
        p.Feet[2] = new V3(p.Hip.X - 3, 0, zh);
        p.Feet[3] = new V3(p.Hip.X - 6, 0, -zh);
        p.Head = new V3(p.Shoulder.X + d.Neck * 0.75, p.Shoulder.Y + d.Neck * 0.85 + d.HeadR * 0.2, 0);
        return p;
    }

    private static Pose BuildPose(Pet pet, Dims d, float t)
    {
        var anim = pet.Animation;
        var pose = anim switch
        {
            "walk" => GaitPose(d, t, run: false),
            "run" => GaitPose(d, t, run: true),
            "sit" => SitPose(d, t),
            "groom" => GroomPose(d, t),
            "petted" => PettedPose(pet, d, t),
            "lie" => LiePose(d, t),
            "eat" => EatPose(d, t),
            "stretch" => StretchPose(d, t),
            "shake" => ShakePose(d, t),
            "dig" => DigPose(d, t),
            "carried" => CarriedPose(d, t),
            "sleep" => SleepPose(pet, d, t),
            "swim" => SwimPose(d, t),
            "stalk" => StalkPose(d, t, moving: true),
            "wiggle" => WigglePose(d, t),
            "pounce" => PouncePose(d, t),
            "bat" => BatPose(d, t),
            "playbow" => PlayBowPose(d, t),
            "hiss" => HissPose(d, t),
            "wrestle" => WrestlePose(d, t),
            "box" => BoxPose(d, t),
            "startle" => StartlePose(d, t),
            "submit" => SubmitPose(d, t),
            "standover" => StandOverPose(d, t),
            _ => IdlePose(d, t),
        };

        if (pet.Animation is not ("eat" or "dig" or "stretch" or "sleep" or "groom"))
            pose.Head = pose.Head + new V3(d.HeadR * 0.25, d.HeadR * 0.4, 0);

        if (pose.Eyes == Eye.Open && (t + Math.Abs(pet.Id.GetHashCode() % 97) * 0.1f) % 4.3f < 0.12f)
            pose.Eyes = Eye.Closed;

        // Airborne (pounce, startle hop): lift the whole rig; the ground shadow stays on the floor.
        if (pet.Lift > 0.01f) Raise(pose, pet.Lift);
        return pose;
    }

    private static void Raise(Pose p, double h)
    {
        V3 Up(V3 v) => new(v.X, v.Y + h, v.Z);
        p.Shoulder = Up(p.Shoulder);
        p.Hip = Up(p.Hip);
        p.Head = Up(p.Head);
        for (var i = 0; i < 4; i++)
        {
            if (p.Feet[i] is { } f) p.Feet[i] = Up(f);
            if (p.Knees[i] is { } k) p.Knees[i] = Up(k);
        }
    }

    // ------------------------------------------------------------------ hunting and social poses

    /// <summary>Hunting crouch: belly low, head level and forward, tail low with a twitching tip.</summary>
    private static Pose StalkPose(Dims d, float t, bool moving)
    {
        var p = moving ? GaitPose(d, t * 0.55f, run: false) : Standing(d);
        var low = d.ShoulderH * 0.38;
        p.Shoulder = new V3(p.Shoulder.X + 2, p.Shoulder.Y - low, 0);
        p.Hip = new V3(p.Hip.X, p.Hip.Y - low * 0.8, 0);
        p.Head = new V3(p.Shoulder.X + d.Neck * 0.9 + d.HeadR * 0.2, p.Shoulder.Y + d.HeadR * 0.1, 0);
        p.HeadPitch = 6;
        p.EarBack = 0.2;
        p.Eyes = Eye.Wide;
        p.TailLift = -0.4;
        p.TailWag = 2.5;
        return p;
    }

    /// <summary>The rear wiggle right before a pounce.</summary>
    private static Pose WigglePose(Dims d, float t)
    {
        var p = StalkPose(d, t, moving: false);
        var w = Math.Sin(t * 18) * 2.6;
        p.Hip = new V3(p.Hip.X, p.Hip.Y + d.ShoulderH * 0.15, w);
        p.TailWag = 5;
        return p;
    }

    /// <summary>Mid-air: stretched out, front paws reaching forward, hind legs trailing.</summary>
    private static Pose PouncePose(Dims d, float t)
    {
        var p = Standing(d);
        p.Shoulder = new V3(p.Shoulder.X + d.BodyR * 0.4, p.Shoulder.Y, 0);
        var reach = (d.L1 + d.L2) * 0.95;
        var zs = d.BodyR * 0.55;
        p.Feet[0] = new V3(p.Shoulder.X + reach, p.Shoulder.Y - 4, zs);
        p.Feet[1] = new V3(p.Shoulder.X + reach * 0.9, p.Shoulder.Y - 6, -zs);
        p.Feet[2] = new V3(p.Hip.X - reach * 0.8, p.Hip.Y * 0.4, zs);
        p.Feet[3] = new V3(p.Hip.X - reach * 0.75, p.Hip.Y * 0.45, -zs);
        p.Head = new V3(p.Shoulder.X + d.Neck + d.HeadR * 0.3, p.Shoulder.Y + d.HeadR * 0.2, 0);
        p.EarBack = 0.6;
        p.Eyes = Eye.Wide;
        p.Mouth = 0.3;
        p.TailLift = 0.2;
        return p;
    }

    /// <summary>Sitting and swiping with one front paw (냥펀치).</summary>
    private static Pose BatPose(Dims d, float t)
    {
        var p = SitPose(d, t);
        var swing = Math.Sin(t * 14);
        var zs = d.BodyR * 0.55;
        p.Feet[0] = new V3(p.Shoulder.X + d.L1 * 0.8 + swing * 6, p.Shoulder.Y - d.L1 * 0.15 + Math.Abs(swing) * 5, zs);
        p.Knees[0] = new V3(p.Shoulder.X + d.L1 * 0.45, p.Shoulder.Y - d.L1 * 0.25, zs);
        p.HeadPitch = 8;
        p.Eyes = Eye.Wide;
        p.EarBack = 0.3;
        return p;
    }

    /// <summary>The play bow: front down, rear up, tail wagging hard, happy face.</summary>
    private static Pose PlayBowPose(Dims d, float t)
    {
        var p = StretchPose(d, 2f);
        p.Eyes = Eye.Open;
        p.Mouth = 0.45;
        p.Tongue = true;
        p.TailWag = 4.5;
        p.TailLift = 0.9;
        p.HeadPitch = -8;
        var bounce = Math.Abs(Math.Sin(t * 6)) * 2.5;
        p.Hip = new V3(p.Hip.X, p.Hip.Y + bounce, 0);
        return p;
    }

    /// <summary>Hiss (cat) or growl (dog): stiff legs, body up, ears flat, mouth open, tail up.</summary>
    private static Pose HissPose(Dims d, float t)
    {
        var p = Standing(d);
        var up = d.BodyR * 0.3;
        p.Shoulder = new V3(p.Shoulder.X - 2, p.Shoulder.Y + up, 0);
        p.Hip = new V3(p.Hip.X + 2, p.Hip.Y + up * 1.2, 0);
        p.Head = new V3(p.Shoulder.X + d.Neck * 0.7, p.Shoulder.Y + d.Neck * 0.4, 0);
        p.HeadPitch = 8;
        p.EarBack = 1;
        p.Eyes = Eye.Wide;
        p.Mouth = 0.75 + Math.Sin(t * 20) * 0.1;
        p.TailLift = 1;
        p.TailWag = 0.4;
        return p;
    }

    /// <summary>Play wrestling: on the side, paws pedalling, mouth open.</summary>
    private static Pose WrestlePose(Dims d, float t)
    {
        var y = d.BodyR * 0.8;
        var p = new Pose
        {
            Hip = new V3(-d.BodyLen * 0.3, y, 0),
            Shoulder = new V3(d.BodyLen * 0.28, y + Math.Sin(t * 7) * 1.5, 0),
            TailOnGround = true,
            TailWag = 3,
            Mouth = 0.5,
            Eyes = Eye.Wide,
        };
        var reach = d.L1 * 0.6 + d.L2 * 0.6;
        for (var i = 0; i < 4; i++)
        {
            var front = i < 2;
            var root = front ? p.Shoulder : p.Hip;
            var ph = t * 11 + i * 1.7;
            var z = d.BodyR * 0.8 + reach * (0.7 + 0.3 * Math.Sin(ph));
            p.Feet[i] = new V3(root.X + Math.Cos(ph) * 6, y + Math.Sin(ph) * 6 + (i % 2) * 4, z);
            p.Knees[i] = new V3(root.X + Math.Cos(ph) * 3, y + 2, d.BodyR * 0.8 + reach * 0.4);
        }
        p.Head = new V3(p.Shoulder.X + d.Neck + d.HeadR * 0.4, d.HeadR * 0.9 + Math.Sin(t * 7) * 2, d.BodyR * 0.3);
        p.HeadYaw = 30;
        p.HeadPitch = -10;
        return p;
    }

    /// <summary>Boxing: reared up on the hind legs, front paws sparring.</summary>
    private static Pose BoxPose(Dims d, float t)
    {
        var hipY = d.HipH * 0.75;
        var p = new Pose
        {
            Hip = new V3(-4, hipY, 0),
            Shoulder = new V3(8, hipY + d.BodyLen * 0.75, 0),
            Mouth = 0.4,
            Eyes = Eye.Open,
            TailWag = 3,
            TailLift = 0.3,
        };
        var zs = d.BodyR * 0.55;
        var zh = d.BodyR * 0.5;
        p.Feet[2] = new V3(p.Hip.X + 4, 0, zh);
        p.Feet[3] = new V3(p.Hip.X - 2, 0, -zh);
        for (var i = 0; i < 2; i++)
        {
            var ph = t * 9 + i * Math.PI;
            p.Feet[i] = new V3(p.Shoulder.X + (d.L1 + d.L2) * (0.55 + 0.25 * Math.Sin(ph)), p.Shoulder.Y - 4 + Math.Cos(ph) * 6, i == 0 ? zs : -zs);
        }
        p.Head = new V3(p.Shoulder.X + d.Neck * 0.8, p.Shoulder.Y + d.Neck * 0.6 + d.HeadR * 0.4, 0);
        p.HeadPitch = 10;
        return p;
    }

    /// <summary>Startled jump: ears back, eyes wide, legs braced.</summary>
    private static Pose StartlePose(Dims d, float t)
    {
        var p = Standing(d);
        p.EarBack = 1;
        p.Eyes = Eye.Wide;
        p.Mouth = 0.35;
        p.TailLift = 1;
        p.HeadPitch = -12;
        return p;
    }

    /// <summary>Lying with the head down and ears back: giving in to the higher-ranked pet.</summary>
    private static Pose SubmitPose(Dims d, float t)
    {
        var p = LiePose(d, t);
        p.HeadPitch = 25;
        p.HeadYaw = 35;
        p.EarBack = 0.9;
        p.TailWag = 2;
        return p;
    }

    /// <summary>Standing tall over the other pet after winning: head and tail up.</summary>
    private static Pose StandOverPose(Dims d, float t)
    {
        var p = Standing(d, 1.5);
        p.HeadPitch = -14;
        p.TailLift = 0.9;
        p.TailWag = 2;
        p.Mouth = 0.3;
        p.Tongue = true;
        return p;
    }

    private static Pose IdlePose(Dims d, float t)
    {
        var p = Standing(d, Math.Sin(t * 1.6) * 0.7);
        p.HeadPitch = Math.Sin(t * 0.7) * 4;
        p.HeadYaw = Math.Sin(t * 0.45) * 10;
        return p;
    }

    private static Pose GaitPose(Dims d, float t, bool run)
    {
        var rate = run ? 2.4 : 1.4;
        var phase = t * rate;
        var stride = d.BodyLen * (run ? 0.36 : 0.21);
        var lift = run ? 11.0 : 5.0;
        var bob = run ? Math.Sin(phase * Math.PI * 2) * 3.2 : Math.Sin(phase * Math.PI * 4) * 1.1;
        var p = Standing(d, bob);

        // Walk: lateral sequence LH, LF, RH, RF. Trot: diagonal pairs.
        double[] ph = run ? new[] { 0.0, 0.5, 0.5, 0.0 } : new[] { 0.75, 0.25, 0.5, 0.0 };
        for (var i = 0; i < 4; i++)
        {
            var rest = p.Feet[i]!.Value;
            var a = (phase + ph[i]) * Math.PI * 2;
            p.Feet[i] = new V3(rest.X + stride * Math.Cos(a), lift * Math.Max(0, -Math.Sin(a)), rest.Z);
        }

        if (run)
        {
            p.Shoulder = new V3(p.Shoulder.X + 3, p.Shoulder.Y - 2 * Math.Sin(phase * Math.PI * 2), 0);
            p.Hip = new V3(p.Hip.X, p.Hip.Y + 2 * Math.Sin(phase * Math.PI * 2), 0);
            p.Head = new V3(p.Head.X + 5, p.Head.Y - 3, 0);
            p.HeadPitch = 6;
            p.EarBack = 1;
            p.Mouth = 0.35;
            p.Tongue = true;
            p.TailLift = 0.4;
            p.TailWag = 2.2;
        }
        else
        {
            p.Head = new V3(p.Head.X, p.Head.Y - Math.Sin(phase * Math.PI * 4) * 0.8, 0);
            p.TailWag = 1.4;
        }
        return p;
    }

    private static Pose SitPose(Dims d, float t)
    {
        var p = new Pose
        {
            Hip = new V3(-d.BodyLen / 2 + 10, d.HipH * 0.45, 0),
            Shoulder = new V3(d.BodyLen / 2 - 12, d.ShoulderH * 1.02 + Math.Sin(t * 1.5) * 0.5, 0),
            HindFolded = true,
            TailOnGround = true,
        };
        var zs = d.BodyR * 0.55;
        p.Feet[0] = new V3(p.Shoulder.X + 2, 0, zs);
        p.Feet[1] = new V3(p.Shoulder.X - 1, 0, -zs);
        var zh = d.BodyR * 0.75;
        p.Knees[2] = new V3(p.Hip.X + d.L1 * 0.45, d.BodyR * 0.45, zh);
        p.Knees[3] = new V3(p.Hip.X + d.L1 * 0.45, d.BodyR * 0.45, -zh);
        p.Feet[2] = new V3(p.Hip.X + d.L1 * 0.45 + d.L2 * 0.8, 0, zh * 0.9);
        p.Feet[3] = new V3(p.Hip.X + d.L1 * 0.45 + d.L2 * 0.8, 0, -zh * 0.9);
        p.Head = new V3(p.Shoulder.X + d.Neck * 0.45, p.Shoulder.Y + d.Neck * 0.9 + d.HeadR * 0.3, 0);
        p.HeadYaw = Math.Sin(t * 0.5) * 6;
        return p;
    }

    private static Pose GroomPose(Dims d, float t)
    {
        var p = SitPose(d, t);
        p.Head = new V3(p.Shoulder.X + d.Neck * 0.35, p.Shoulder.Y + d.HeadR * 0.2 + Math.Sin(t * 9) * 1.2, 0);
        p.HeadPitch = 55 + Math.Sin(t * 9) * 5;
        p.HeadYaw = 20;
        p.Eyes = Eye.Closed;
        p.Tongue = true;
        p.Mouth = 0.15;
        return p;
    }

    private static Pose PettedPose(Pet pet, Dims d, float t)
    {
        var p = SitPose(d, t);
        p.Eyes = Eye.Happy;
        p.HeadPitch = -12 + Math.Sin(t * 3) * 4;
        p.HeadYaw = Math.Sin(t * 2) * 8;
        if (pet.Species == PetSpecies.Dog)
        {
            p.Mouth = 0.4;
            p.Tongue = true;
            p.TailWag = 4;
        }
        return p;
    }

    private static Pose LiePose(Dims d, float t)
    {
        var breath = Math.Sin(t * 1.4) * 0.5;
        var p = new Pose
        {
            Hip = new V3(-d.BodyLen / 2 + 8, d.HipH * 0.38 + breath, 0),
            Shoulder = new V3(d.BodyLen / 2 - 8, d.ShoulderH * 0.46 + breath, 0),
            HindFolded = true,
            TailOnGround = true,
            TailWag = 0.6,
        };
        var zs = d.BodyR * 0.6;
        p.Knees[0] = new V3(p.Shoulder.X + 3, d.BodyR * 0.45, zs);
        p.Knees[1] = new V3(p.Shoulder.X + 3, d.BodyR * 0.45, -zs);
        p.Feet[0] = new V3(p.Shoulder.X + d.L1 * 0.5 + d.L2 * 0.8, 0, zs);
        p.Feet[1] = new V3(p.Shoulder.X + d.L1 * 0.5 + d.L2 * 0.8, 0, -zs);
        var zh = d.BodyR * 0.95;
        p.Knees[2] = new V3(p.Hip.X + d.L1 * 0.4, d.BodyR * 0.4, zh);
        p.Knees[3] = new V3(p.Hip.X + d.L1 * 0.4, d.BodyR * 0.4, -zh);
        p.Feet[2] = new V3(p.Hip.X + d.L1 * 0.4 + d.L2 * 0.7, 0, zh * 0.85);
        p.Feet[3] = new V3(p.Hip.X + d.L1 * 0.4 + d.L2 * 0.7, 0, -zh * 0.85);
        p.Head = new V3(p.Shoulder.X + d.Neck * 0.7, p.Shoulder.Y + d.Neck * 0.85 + d.HeadR * 0.5, 0);
        p.HeadPitch = 4;
        return p;
    }

    private static Pose EatPose(Dims d, float t)
    {
        var p = Standing(d);
        p.Shoulder = new V3(p.Shoulder.X, p.Shoulder.Y - 3, 0);
        var chew = Math.Sin(t * 12) * 1.2;
        p.Head = new V3(p.Shoulder.X + d.Neck * 0.8 + d.HeadR * 0.6, d.HeadR * 0.95 + 2 - chew, 0);
        p.HeadPitch = 45;
        p.Mouth = 0.3 + 0.3 * Math.Sin(t * 12);
        p.TailWag = 2.5;
        p.TailLift = 0.3;
        return p;
    }

    private static Pose StretchPose(Dims d, float t)
    {
        var p = new Pose
        {
            Shoulder = new V3(d.BodyLen / 2 - 2, d.ShoulderH * 0.5, 0),
            Hip = new V3(-d.BodyLen / 2 + 6, d.HipH * 1.06, 0),
            Eyes = Eye.Closed,
            Mouth = t < 1.2f ? 0.7 : 0,
            TailLift = 0.8,
        };
        var zs = d.BodyR * 0.55;
        var zh = d.BodyR * 0.5;
        p.Feet[0] = new V3(p.Shoulder.X + d.L1 + d.L2 * 0.7, 0, zs);
        p.Feet[1] = new V3(p.Shoulder.X + d.L1 + d.L2 * 0.5, 0, -zs);
        p.Feet[2] = new V3(p.Hip.X - 3, 0, zh);
        p.Feet[3] = new V3(p.Hip.X - 6, 0, -zh);
        p.Head = new V3(p.Shoulder.X + d.Neck * 0.9 + d.HeadR * 0.5, d.ShoulderH * 0.45, 0);
        p.HeadPitch = 12;
        return p;
    }

    private static Pose ShakePose(Dims d, float t)
    {
        var p = Standing(d);
        var w = Math.Sin(t * 40) * 4;
        p.Shoulder = new V3(p.Shoulder.X, p.Shoulder.Y, w);
        p.Hip = new V3(p.Hip.X, p.Hip.Y, -w);
        p.Head = new V3(p.Head.X, p.Head.Y, w * 1.5);
        p.HeadYaw = Math.Sin(t * 40) * 25;
        p.Eyes = Eye.Closed;
        p.EarFlap = true;
        p.TailWag = 6;
        return p;
    }

    private static Pose DigPose(Dims d, float t)
    {
        var p = Standing(d);
        p.Shoulder = new V3(p.Shoulder.X, p.Shoulder.Y - 6, 0);
        var a = t * 16;
        var r0 = p.Feet[0]!.Value;
        var r1 = p.Feet[1]!.Value;
        p.Feet[0] = new V3(r0.X + 10 + 9 * Math.Cos(a), 7 * Math.Max(0, Math.Sin(a)), r0.Z);
        p.Feet[1] = new V3(r1.X + 10 + 9 * Math.Cos(a + Math.PI), 7 * Math.Max(0, Math.Sin(a + Math.PI)), r1.Z);
        p.Head = new V3(p.Shoulder.X + d.Neck * 0.7 + d.HeadR * 0.5, d.ShoulderH * 0.4, 0);
        p.HeadPitch = 35;
        p.TailWag = 3;
        p.TailLift = 0.5;
        return p;
    }

    /// <summary>Dog paddle: the body sinks below the waterline (the 3D renderer clips everything under y = 0), head up.</summary>
    private static Pose SwimPose(Dims d, float t)
    {
        var p = GaitPose(d, t * 0.9f, run: false);
        var sink = d.ShoulderH * 0.85;
        var bob = Math.Sin(t * 3.0) * 1.2;
        V3 Down(V3 v) => new(v.X, v.Y - sink + bob, v.Z);
        p.Shoulder = Down(p.Shoulder);
        p.Hip = Down(p.Hip);
        p.Head = new V3(p.Head.X + d.HeadR * 0.2, p.Head.Y - sink + bob + d.HeadR * 0.35, p.Head.Z);
        for (var i = 0; i < 4; i++)
            if (p.Feet[i] is { } f) p.Feet[i] = Down(f);
        p.HeadPitch = -10;
        p.TailOnGround = false;
        p.TailLift = 0.1;
        p.TailWag = 1.2;
        p.Mouth = 0.25;
        p.EarBack = 0.3;
        return p;
    }

    private static Pose CarriedPose(Dims d, float t)
    {
        var sway = Math.Sin(t * 2.2) * 3;
        var hipY = (d.L1 + d.L2) * 0.85;
        var p = new Pose
        {
            Hip = new V3(-2, hipY, sway * 0.3),
            Shoulder = new V3(3, hipY + d.BodyLen * 0.85, sway * 0.2),
            Eyes = Eye.Wide,
            TailLift = -1,
            TailWag = 0.3,
        };
        var zs = d.BodyR * 0.55;
        var zh = d.BodyR * 0.5;
        p.Feet[0] = new V3(p.Shoulder.X + 5 + sway, p.Shoulder.Y - d.L1 - d.L2 + 4, zs);
        p.Feet[1] = new V3(p.Shoulder.X - 2 + sway, p.Shoulder.Y - d.L1 - d.L2 + 5, -zs);
        p.Feet[2] = new V3(p.Hip.X + 4 + sway, 0, zh);
        p.Feet[3] = new V3(p.Hip.X - 3 + sway, 1, -zh);
        p.Head = new V3(p.Shoulder.X + 6, p.Shoulder.Y + d.Neck * 0.7 + d.HeadR * 0.6, 0);
        p.HeadPitch = 12;
        return p;
    }

    private static Pose SleepPose(Pet pet, Dims d, float t)
    {
        var breath = Math.Sin(t * 1.3) * 0.5;
        if (pet.Species == PetSpecies.Cat)
        {
            return new Pose { CurledSleep = true, Eyes = Eye.Closed, Hip = new V3(0, breath, 0) };
        }

        // Dog on its side: trunk lying low, legs toward +z (the viewer when facing right).
        var p = new Pose
        {
            SideSleep = true,
            Eyes = Eye.Closed,
            Hip = new V3(-d.BodyLen * 0.3, d.BodyR * 0.8 + breath, 0),
            Shoulder = new V3(d.BodyLen * 0.28, d.BodyR * 0.8 + breath, 0),
            TailOnGround = true,
            TailWag = 0.2,
        };
        p.Knees[0] = new V3(p.Shoulder.X + 2, d.BodyR * 0.55, d.BodyR * 0.9 + d.L1 * 0.5);
        p.Feet[0] = new V3(p.Shoulder.X + 6, d.BodyR * 0.3, d.BodyR * 0.9 + d.L1 * 0.5 + d.L2 * 0.8);
        p.Knees[1] = new V3(p.Shoulder.X - 4, d.BodyR * 1.1, d.BodyR * 0.6 + d.L1 * 0.4);
        p.Feet[1] = new V3(p.Shoulder.X - 1, d.BodyR * 0.9, d.BodyR * 0.6 + d.L1 * 0.4 + d.L2 * 0.7);
        p.Knees[2] = new V3(p.Hip.X - 2, d.BodyR * 0.5, d.BodyR * 0.9 + d.L1 * 0.5);
        p.Feet[2] = new V3(p.Hip.X + 4, d.BodyR * 0.3, d.BodyR * 0.9 + d.L1 * 0.5 + d.L2 * 0.7);
        p.Knees[3] = new V3(p.Hip.X - 6, d.BodyR * 1.0, d.BodyR * 0.6 + d.L1 * 0.4);
        p.Feet[3] = new V3(p.Hip.X - 2, d.BodyR * 0.8, d.BodyR * 0.6 + d.L1 * 0.4 + d.L2 * 0.6);
        p.Head = new V3(p.Shoulder.X + d.Neck * 0.9 + d.HeadR * 0.4, d.HeadR * 0.85, d.BodyR * 0.2);
        p.HeadPitch = 10;
        p.HeadYaw = 25;
        return p;
    }

    /// <summary>Turn the head toward the look target (screen pixels relative to feet) within the neck's range.</summary>
    private static void ApplyLook(Pose pose, Camera cam, Dims d, Vec2? look, Pet pet)
    {
        var anim = pet.Animation;
        if (anim == "carried")
        {
            // Face the person holding you.
            var dir = cam.ScreenDirToModel(0, 1);
            pose.HeadYaw = Math.Clamp(Math.Atan2(dir.Z, dir.X) * 180 / Math.PI, -80, 80);
            return;
        }

        if (look is not { } l || anim is not ("idle" or "walk" or "sit" or "lie" or "petted" or "run"))
        {
            if (Toon) FaceViewer(pose, cam, anim);
            return;
        }

        var headScreen = cam.Project(pose.Head);
        var dx = l.X - headScreen.X;
        var dy = l.Y - headScreen.Y;
        var m = cam.ScreenDirToModel(dx, dy * 1.3);
        var yaw = Math.Atan2(m.Z, m.X) * 180 / Math.PI;
        pose.HeadYaw = Math.Clamp(yaw, -85, 85) * 0.8;
        var horiz = Math.Max(30, Math.Sqrt(dx * dx + dy * dy));
        pose.HeadPitch = Math.Clamp(dy / horiz * 45, -30, 40) * 0.7;
    }

    /// <summary>
    /// Drawn-cartoon cheat: a profile shows the face three-quarter on, the way an animator would draw it,
    /// instead of a flat side view with one eye on the silhouette edge. Seen from behind nothing changes.
    /// </summary>
    private static void FaceViewer(Pose pose, Camera cam, string anim)
    {
        if (anim is not ("idle" or "walk" or "sit" or "lie" or "petted" or "run" or "stretch")) return;
        var dir = cam.ScreenDirToModel(0, 1);
        var camYaw = Math.Atan2(dir.Z, dir.X) * 180 / Math.PI;   // 0 = the viewer is straight ahead of the nose
        var away = Math.Abs(camYaw);
        if (away >= 125) return;
        var w = away <= 95 ? 0.7 : 0.7 * (125 - away) / 30;
        var want = Math.Clamp(camYaw, -55, 55);
        pose.HeadYaw += (want - pose.HeadYaw) * w;
    }

    // ------------------------------------------------------------------ scene assembly

    private static void BuildScene(Scene s, Pet pet, Dims d, Style style, Pose p, float t)
    {
        if (p.CurledSleep)
        {
            BuildCurledCat(s, d, style, p, t);
            return;
        }

        var fur = style.Fur;
        var mud = pet.Appearance.Mud;
        var pawColor = mud > 0.01f ? Mix(style.Paw, MudColor(pet), Math.Min(1, mud + 0.3)) : style.Paw;

        // Legs: upper (joint → knee) and lower (knee → foot) tubes, paw spheres.
        var sz = d.BodyR * 0.55;
        var hz = d.BodyR * 0.5;
        V3[] joints =
        {
            new(p.Shoulder.X, p.Shoulder.Y - d.BodyR * 0.25, sz),
            new(p.Shoulder.X, p.Shoulder.Y - d.BodyR * 0.25, -sz),
            new(p.Hip.X, p.Hip.Y - d.BodyR * 0.25, hz),
            new(p.Hip.X, p.Hip.Y - d.BodyR * 0.25, -hz),
        };

        s.K = 1.2;
        for (var i = 0; i < 4; i++)
        {
            if (p.Feet[i] is not { } foot) continue;
            var joint = joints[i];
            if (p.HindFolded && i >= 2)
                joint = new V3(p.Hip.X, p.Hip.Y, Math.Sign(joint.Z) * d.BodyR * 0.75);

            var knee = p.Knees[i] ?? SolveKnee(joint, foot, d.L1, d.L2);
            var upperR = d.BodyR * (i < 2 ? 0.3 : 0.34);
            var lowerR = d.BodyR * 0.24;
            s.Capsule(joint, knee, upperR, style.Leg, k: 2.5);
            s.Capsule(knee, foot + new V3(0, d.BodyR * 0.2, 0), lowerR, style.Leg, k: 1.2);
            s.Sphere(foot + new V3(1.5, d.BodyR * 0.28, 0), d.BodyR * 0.42, pawColor, k: 1.2);
        }

        // Folded haunches.
        s.K = 3;
        if (p.HindFolded)
        {
            s.Sphere(new V3(p.Hip.X + 1, p.Hip.Y, d.BodyR * 0.55), d.BodyR * 0.95, fur);
            s.Sphere(new V3(p.Hip.X + 1, p.Hip.Y, -d.BodyR * 0.55), d.BodyR * 0.95, fur);
        }
        s.K = 4;

        // Trunk + chest + rump.
        var cat = pet.Species == PetSpecies.Cat;
        s.Stripe = cat ? 1 : 0;
        var trunkA = new V3(p.Hip.X + 2, p.Hip.Y + d.BodyR * 0.2, 0);
        var trunkB = new V3(p.Shoulder.X - 2, p.Shoulder.Y + d.BodyR * 0.2, 0);
        var kitten = cat && Toon;
        s.Capsule(trunkA, trunkB, d.BodyR * (kitten ? 0.94 : 1), fur);
        s.Sphere(kitten ? trunkA + new V3(-1, -d.BodyR * 0.08, 0) : trunkA, d.BodyR * (kitten ? 1.14 : 1.02), fur);
        s.Sphere(trunkB, d.BodyR * (kitten ? 1.1 : 1.06), fur);
        // Belly highlight (lighter, slightly lower), mud belly patch when dirty.
        var bellyColor = mud >= 0.3f ? Mix(style.Belly, MudColor(pet), 0.7) : style.Belly;
        s.Stripe = 0;
        s.Capsule(trunkA + new V3(4, -d.BodyR * 0.45, 0), trunkB + new V3(-4, -d.BodyR * 0.45, 0), d.BodyR * 0.55, bellyColor, k: 3);

        // Coat pattern: darker saddle along the back (sable / tabby), pale chest and neck ruff.
        var pom = pet.Species == PetSpecies.Dog;
        s.Stripe = cat ? 1 : 0;
        s.Capsule(trunkA + new V3(0, d.BodyR * 0.55, 0), trunkB + new V3(-d.BodyR * 0.4, d.BodyR * 0.5, 0), d.BodyR * 0.62, style.Saddle, k: 2.5);
        s.Stripe = 0;
        s.Sphere(trunkB + new V3(d.BodyR * 0.55, -d.BodyR * 0.2, 0), d.BodyR * (pom ? 0.95 : 0.95), style.Chest, k: 4);
        s.Sphere(V3.Lerp(trunkB, p.Head, 0.4) + new V3(d.BodyR * 0.2, -d.BodyR * 0.25, 0), d.BodyR * (pom ? 0.85 : 0.8), style.Chest, k: 4);
        if (mud >= 0.7f)
            s.Capsule(trunkA + new V3(6, d.BodyR * 0.6, 0), trunkB + new V3(-6, d.BodyR * 0.6, 0), d.BodyR * 0.35, MudColor(pet), k: 1);

        // Tail.
        s.Stripe = cat ? 1 : 0;
        s.K = 2.5;
        BuildTail(s, pet.Species, d, style, p, t);

        // Neck.
        s.Stripe = cat ? 0.5 : 0;
        s.K = 4;
        var neckFrom = trunkB + new V3(0, d.BodyR * 0.1, 0);
        var neckTo = p.Head + new V3(-d.HeadR * 0.3, -d.HeadR * 0.2, 0);
        s.Capsule(neckFrom, neckTo, d.BodyR * 0.58, fur);
        if (cat && Toon) BuildCollar(s, d, V3.Lerp(neckFrom, neckTo, 0.42), (neckTo - neckFrom).Normalized);

        BuildHead(s, pet, d, style, p, t);
    }

    /// <summary>A brown collar ring around the neck with a little heart tag hanging at the front.</summary>
    private static void BuildCollar(Scene s, Dims d, V3 center, V3 axis)
    {
        var collar = Color.FromRgb(0xB8, 0x6E, 0x3C);
        var tagColor = Color.FromRgb(0x96, 0x78, 0x66);
        var u = Cross(axis, new V3(0, 0, 1)).Normalized;
        if (u.Length < 0.5) u = new V3(1, 0, 0);
        var w = Cross(axis, u).Normalized;
        var ring = d.BodyR * 0.66;
        const int n = 8;
        for (var i = 0; i < n; i++)
        {
            double a0 = i * Math.PI * 2 / n, a1 = (i + 1) * Math.PI * 2 / n;
            var p0 = center + (u * Math.Cos(a0) + w * Math.Sin(a0)) * ring;
            var p1 = center + (u * Math.Cos(a1) + w * Math.Sin(a1)) * ring;
            s.AddSdf(p0, p1, d.BodyR * 0.13, d.BodyR * 0.13, collar, k: 0, gloss: 0.2);
        }

        // Front of the neck: the direction in the collar plane closest to forward-and-down.
        var want = new V3(1, -0.6, 0);
        var front = (want - axis * (want.X * axis.X + want.Y * axis.Y + want.Z * axis.Z)).Normalized;
        var side = Cross(axis, front).Normalized;
        var tag = center + front * (ring + d.BodyR * 0.2) - axis * (d.BodyR * 0.25);
        var lobe = d.BodyR * 0.15;
        s.AddSdf(tag + side * (lobe * 0.8) + axis * (lobe * 0.4), tag + side * (lobe * 0.8) + axis * (lobe * 0.4), lobe, lobe, tagColor, k: 0, gloss: 0.7);
        s.AddSdf(tag - side * (lobe * 0.8) + axis * (lobe * 0.4), tag - side * (lobe * 0.8) + axis * (lobe * 0.4), lobe, lobe, tagColor, k: 0, gloss: 0.7);
        s.AddSdf(tag, tag - axis * (lobe * 1.6), lobe * 1.15, lobe * 0.15, tagColor, k: 0, gloss: 0.7);
    }

    private static V3 SolveKnee(V3 joint, V3 foot, double l1, double l2)
    {
        // Two-bone IK in the vertical plane through joint and foot; knee bends backwards (-x).
        var dx = foot.X - joint.X;
        var dy = foot.Y - joint.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        var reach = l1 + l2 - 0.5;
        if (dist > reach)
        {
            dx *= reach / dist;
            dy *= reach / dist;
            dist = reach;
        }
        dist = Math.Max(dist, 1);
        var cosA = Math.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist), -1, 1);
        var a = Math.Acos(cosA);
        var baseAngle = Math.Atan2(dy, dx);
        var k1 = new V3(joint.X + l1 * Math.Cos(baseAngle + a), joint.Y + l1 * Math.Sin(baseAngle + a), 0);
        var k2 = new V3(joint.X + l1 * Math.Cos(baseAngle - a), joint.Y + l1 * Math.Sin(baseAngle - a), 0);
        var k = k1.X < k2.X ? k1 : k2;
        return new V3(k.X, k.Y, (joint.Z + foot.Z) / 2);
    }

    private static void BuildTail(Scene s, PetSpecies species, Dims d, Style style, Pose p, float t)
    {
        var root = new V3(p.Hip.X - d.BodyR * 0.5, p.Hip.Y + d.BodyR * 0.55, 0);
        var sway = Math.Sin(t * 2.5 * p.TailWag) * (0.25 + 0.15 * Math.Min(3, p.TailWag));
        var seg = d.BodyLen * 0.22;
        var pts = new List<V3> { root };

        if (species == PetSpecies.Dog)
        {
            // Pomeranian: a big plume curled up and forward over the back; hangs only when carried.
            if (p.TailLift < -0.5)
            {
                pts.Add(root + new V3(-3, -seg, seg * 0.3 * sway));
                pts.Add(root + new V3(-4, -seg * 1.7, seg * 0.5 * sway));
            }
            else
            {
                pts.Add(root + new V3(-seg * 0.15, seg * 0.55, seg * 0.3 * sway));
                pts.Add(root + new V3(seg * 0.55, seg * 0.8, seg * 0.5 * sway));
                pts.Add(root + new V3(seg * 1.2, seg * 0.6, seg * 0.6 * sway));
            }
            var colors = new[] { style.Fur, style.Fur, Mix(style.Fur, style.Chest, 0.6) };
            for (var i = 0; i < pts.Count - 1; i++)
            {
                var ra = d.BodyR * (0.5 - 0.06 * i);
                var rb = d.BodyR * (0.5 - 0.06 * (i + 1));
                s.Capsule(pts[i], pts[i + 1], (ra + rb) / 2, colors[Math.Min(i, colors.Length - 1)]);
            }
            return;
        }

        // Kitten tail: long, upright, with faint tabby rings.
        seg *= 1.5;
        if (p.TailOnGround)
        {
            pts.Add(root + new V3(-seg * 0.8, -root.Y * 0.5, seg * 0.3 * sway));
            pts.Add(root + new V3(-seg * 1.4, -root.Y + 3.5, seg * 0.8 * sway + d.BodyR * 0.4));
            pts.Add(root + new V3(-seg * 1.1, -root.Y + 3.5, seg * 1.4 * sway + d.BodyR * 1.1));
        }
        else if (p.TailLift < -0.5)
        {
            pts.Add(root + new V3(-3, -seg, seg * 0.3 * sway));
            pts.Add(root + new V3(-4, -seg * 1.8, seg * 0.5 * sway));
        }
        else
        {
            var lift = p.TailLift * 5;
            pts.Add(root + new V3(-seg * 0.8, seg * 0.45 + lift, seg * 0.4 * sway));
            pts.Add(root + new V3(-seg * 1.25, seg * 1.15 + lift, seg * 0.9 * sway));
            pts.Add(root + new V3(-seg * 1.1, seg * 1.8 + lift, seg * 1.3 * sway));
        }
        var thick = Toon ? 0.44 : 0.36;
        var taper = Toon ? 0.04 : 0.05;
        for (var i = 0; i < pts.Count - 1; i++)
            s.Capsule(pts[i], pts[i + 1], d.BodyR * (thick - taper * i), i % 2 == 0 ? style.Fur : style.Saddle);
        if (Toon)
            s.Sphere(pts[^1], d.BodyR * (thick - taper * (pts.Count - 1)) * 1.05, style.Saddle);
    }

    private static void BuildHead(Scene s, Pet pet, Dims d, Style style, Pose p, float t)
    {
        var r = d.HeadR;
        var h = p.Head;
        var yaw = p.HeadYaw * Math.PI / 180;
        var pitch = p.HeadPitch * Math.PI / 180;

        // Head basis in model space.
        var fwd = new V3(Math.Cos(pitch) * Math.Cos(yaw), -Math.Sin(pitch), Math.Cos(pitch) * Math.Sin(yaw));
        var side = new V3(-Math.Sin(yaw), 0, Math.Cos(yaw));
        var up = new V3(Math.Sin(pitch) * Math.Cos(yaw), Math.Cos(pitch), Math.Sin(pitch) * Math.Sin(yaw));

        V3 At(double f, double u, double sd) => h + fwd * (f * r) + up * (u * r) + side * (sd * r);

        s.HeadCenter = h;
        var persian = pet.Species == PetSpecies.Cat;
        s.Stripe = persian && !Toon ? 0.8 : 0;
        s.Sphere(h, r, style.Fur, k: 3);
        s.Stripe = 0;

        // Fluffy cheeks in the chest colour; Persian gets a darker tabby crown.
        var cheek = persian ? style.Chest : Mix(style.Fur, style.Chest, 0.35);
        s.Sphere(At(0.3, -0.3, 0.55), r * 0.5, cheek, edge: false, k: 2.5);
        s.Sphere(At(0.3, -0.3, -0.55), r * 0.5, cheek, edge: false, k: 2.5);

        // Muzzle and nose: Persian face is flat with the nose high between the eyes; Pomeranian has a short fox muzzle.
        if (persian)
        {
            s.Sphere(At(0.74, -0.3, 0.22), r * 0.3, style.Belly, edge: false, k: 1.5);
            s.Sphere(At(0.74, -0.3, -0.22), r * 0.3, style.Belly, edge: false, k: 1.5);
            s.Sphere(At(0.6, -0.52, 0), r * 0.24, style.Belly, edge: false, k: 1.5);
            s.Sphere(At(0.97, -0.08, 0), r * 0.1, style.Nose, edge: false, k: 0, gloss: 0.9);

            if (Toon)
            {
                // Pointy fur tufts on the cheeks and the crown, like a drawn kitten.
                foreach (var sign in new[] { -1, 1 })
                {
                    s.AddSdf(At(0.15, -0.25, sign * 0.8), At(-0.05, -0.52, sign * 1.17), r * 0.2, r * 0.03, style.Fur, k: 1.2);
                    s.AddSdf(At(0.3, -0.45, sign * 0.66), At(0.2, -0.72, sign * 1.02), r * 0.18, r * 0.02, style.Fur, k: 1.2);
                    s.AddSdf(At(-0.05, 0.0, sign * 0.88), At(-0.3, -0.16, sign * 1.15), r * 0.15, r * 0.03, style.Fur, k: 1.2);
                }
                s.AddSdf(At(-0.15, 0.85, 0.0), At(0.05, 1.15, 0.06), r * 0.14, r * 0.02, style.Fur, k: 1.2);

                // Tabby marks on the forehead: a middle stripe and one on each side.
                // They carry on over the crown to the back of the head so every view shows clean tabby lines.
                s.AddSdf(At(0.5, 0.86, 0), At(0.8, 0.6, 0), r * 0.07, r * 0.05, style.Saddle, k: 0, gloss: 0);
                s.AddSdf(At(0.0, 1.0, 0), At(0.5, 0.86, 0), r * 0.08, r * 0.07, style.Saddle, k: 0, gloss: 0);
                s.AddSdf(At(-0.56, 0.83, 0), At(0.0, 1.0, 0), r * 0.07, r * 0.08, style.Saddle, k: 0, gloss: 0);
                foreach (var sign in new[] { -1, 1 })
                {
                    s.AddSdf(At(0.42, 0.84, sign * 0.33), At(0.7, 0.62, sign * 0.35), r * 0.06, r * 0.04, style.Saddle, k: 0, gloss: 0);
                    s.AddSdf(At(-0.05, 0.93, sign * 0.36), At(0.42, 0.84, sign * 0.33), r * 0.065, r * 0.06, style.Saddle, k: 0, gloss: 0);
                    s.AddSdf(At(-0.62, 0.62, sign * 0.42), At(-0.05, 0.93, sign * 0.36), r * 0.05, r * 0.065, style.Saddle, k: 0, gloss: 0);
                }

                // Little "w" mouth under the nose and whisker dots on the muzzle.
                var line = Color.FromRgb(0x7A, 0x40, 0x22);
                foreach (var sign in new[] { -1, 1 })
                {
                    var corner = At(1.0, -0.4, sign * 0.12);
                    s.AddSdf(At(0.96, -0.3, 0), corner, r * 0.028, r * 0.028, line, k: 0, gloss: 0);
                    s.AddSdf(corner, At(1.02, -0.34, sign * 0.22), r * 0.028, r * 0.028, line, k: 0, gloss: 0);
                    s.Sphere(At(1.02, -0.22, sign * 0.3), r * 0.035, line, edge: false, k: 0, gloss: 0);
                    s.Sphere(At(1.0, -0.3, sign * 0.34), r * 0.035, line, edge: false, k: 0, gloss: 0);
                }
            }
        }
        else
        {
            s.Sphere(At(0.78, -0.22, 0), r * 0.42, style.Chest, edge: false, k: 2);
            s.Sphere(At(1.17, -0.14, 0), r * 0.17, style.Nose, edge: false, k: 0, gloss: 0.9);
        }
        if (p.Mouth > 0.05)
            s.Sphere(At(0.95, -0.52, 0), r * 0.22 * p.Mouth + r * 0.08, Color.FromRgb(0x4A, 0x2A, 0x2A), edge: false, k: 0);
        if (p.Tongue)
            s.Sphere(At(0.95, -0.68, 0), r * 0.2, Color.FromRgb(0xE8, 0x70, 0x7F), edge: false, k: 0, gloss: 0.5);
        s.K = 1.5;

        // Ears.
        var flap = p.EarFlap ? Math.Sin(t * 40) : 0;
        foreach (var sign in new[] { -1, 1 })
        {
            var tilt = p.EarBack * 0.5 + flap * 0.12;
            if (persian)
            {
                // Small, round-tipped ears set low and wide, half buried in fur.
                var b1 = At(0.12, 0.55, sign * 0.82);
                var b2 = At(-0.42, 0.6, sign * 0.32);
                var tip = At(-0.15 - tilt, 1.42 - tilt * 0.4, sign * 0.8);
                s.Tri(b1, tip, b2, style.Fur, style.EarInner, fwd * (r * 0.2), tipRound: Toon ? 0.12 : 0.3);
            }
            else
            {
                // Pomeranian: small pointed fox ears standing up.
                var b1 = At(0.05, 0.62, sign * 0.72);
                var b2 = At(-0.38, 0.7, sign * 0.32);
                var tip = At(-0.12 - tilt, 1.5 - tilt * 0.4, sign * 0.62);
                s.Tri(b1, tip, b2, style.Fur, style.EarInner, fwd * (r * 0.18), tipRound: 0.2);
            }
        }

        // Eyes: two, each sorted by its own depth so the far one hides behind the head.
        var pupilDir = fwd;
        var faceAway = s.Cam.World(h + fwd).zr - s.Cam.World(h).zr;   // < 0: the face points away from the viewer
        foreach (var sign in new[] { -1, 1 })
        {
            var c = At(0.68, 0.3, sign * 0.42);
            var lidA = At(0.6, 0.3, sign * 0.62);
            var lidB = At(0.78, 0.3, sign * 0.22);
            s.EyeAt(c, r * 0.21, p.Eyes, pupilDir, lidA, lidB);

            // 3D eye: a glossy white ball set into the head, a dark pupil ball in front of it.
            var ec = persian ? (Toon ? At(0.6, 0.02, sign * 0.42) : At(0.66, 0.12, sign * 0.38)) : At(0.68, 0.16, sign * 0.38);
            var outward = (ec - h).Normalized;
            // The cartoon eye pokes out past the head outline, so an eye on the far side of a face turned
            // away from the viewer would show as a dark notch on the back of the head. Leave it out.
            if (Toon && (faceAway < -0.2 || s.Cam.World(h + outward).zr - s.Cam.World(h).zr < -0.3)) continue;
            var lid = Cross(up, outward).Normalized;
            var lash = Color.FromRgb(0x1A, 0x14, 0x14);
            switch (p.Eyes)
            {
                case Eye.Closed:
                    s.AddSdf(ec - lid * (r * 0.26) + outward * (r * 0.3), ec + lid * (r * 0.26) + outward * (r * 0.3), r * 0.05, r * 0.05, lash, k: 0, gloss: 0);
                    break;
                case Eye.Happy:
                {
                    var top = ec + up * (r * 0.14) + outward * (r * 0.34);
                    s.AddSdf(ec - lid * (r * 0.26) + outward * (r * 0.28), top, r * 0.05, r * 0.05, lash, k: 0, gloss: 0);
                    s.AddSdf(top, ec + lid * (r * 0.26) + outward * (r * 0.28), r * 0.05, r * 0.05, lash, k: 0, gloss: 0);
                    break;
                }
                default:
                {
                    var wide = p.Eyes == Eye.Wide;
                    var er = r * (persian ? (Toon ? (wide ? 0.48 : 0.44) : (wide ? 0.42 : 0.38)) : (wide ? 0.34 : 0.31));
                    var gaze = (fwd * 0.9 + outward * 0.3 + up * 0.05).Normalized;
                    // Clip to just outside the head (the cartoon face is puffed up by cheeks and tufts).
                    s.AddSdf(ec, h, er, r * (Toon && persian ? 1.12 : 1.03), style.Iris, k: 0, eyeDir: gaze);
                    break;
                }
            }
        }

        // Whiskers fanning out from the muzzle (drawn as lines on top of the 3D body). The cartoon kitten has dots instead.
        if (Toon && (persian || faceAway < -0.1)) return;
        var root = persian ? At(0.82, -0.3, 0) : At(1.0, -0.2, 0);
        var len = persian ? r * 1.5 : r * 0.8;
        foreach (var sign in new[] { -1, 1 })
        {
            for (var i = 0; i < 3; i++)
            {
                var a = root + side * (sign * r * 0.3) + up * (r * (0.06 - 0.09 * i));
                var b = a + side * (sign * len * 0.85) + fwd * (len * 0.3) + up * (r * (0.22 - 0.3 * i));
                s.Whiskers.Add((a, b));
            }
        }
    }

    private static void BuildCurledCat(Scene s, Dims d, Style style, Pose p, float t)
    {
        // A ring of trunk segments lying on the ground, head resting on the front, tail wrapped round.
        var R = d.BodyLen * 0.27;
        var y = d.BodyR * 0.85 + p.Hip.Y;
        var pts = new List<V3>();
        for (var i = 0; i <= 6; i++)
        {
            var a = Math.PI * 0.15 + i * (Math.PI * 1.45 / 6);
            pts.Add(new V3(Math.Cos(a) * R, y, Math.Sin(a) * R));
        }
        s.Chain(pts, d.BodyR * 0.95, d.BodyR * 0.8, style.Fur);
        s.Sphere(pts[0], d.BodyR * 0.98, style.Fur);

        var tail = new List<V3>();
        for (var i = 0; i <= 4; i++)
        {
            var a = Math.PI * 1.6 + i * (Math.PI * 0.55 / 4);
            tail.Add(new V3(Math.Cos(a) * R * 1.05, d.BodyR * 0.3, Math.Sin(a) * R * 1.05));
        }
        s.Chain(tail, d.BodyR * 0.26, d.BodyR * 0.16, style.Fur);

        var head = pts[0] + new V3(d.HeadR * 0.6, d.HeadR * 0.15, d.HeadR * 0.8);
        var pose = new Pose { Head = head, HeadYaw = 70, HeadPitch = 25, Eyes = Eye.Closed };
        var pet = new Pet(PetSpecies.Cat, "", DateTime.UtcNow, new Random(0));
        BuildHead(s, pet, d, style, pose, t);
    }

    // ------------------------------------------------------------------ 2D particles on top

    private static void DrawParticles(DrawingContext ctx, Pet pet, Dims d, Point o, float t, Camera cam)
    {
        var wet = pet.Appearance.Wet;
        if (wet > 0.05f)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(160 * wet), 0x6F, 0xB7, 0xFF));
            for (var i = 0; i < 3; i++)
            {
                var phase = (t * 0.8f + i * 0.37f) % 1f;
                var x = o.X - d.BodyLen * 0.3 + i * d.BodyLen * 0.3;
                var y = o.Y - d.ShoulderH * 0.6 + phase * d.ShoulderH * 0.7;
                ctx.DrawEllipse(brush, null, new Point(x, y), 2, 3);
            }
        }

        switch (pet.Animation)
        {
            case "swim":
            {
                // Ripples spreading on the water surface around the swimming pet.
                for (var i = 0; i < 3; i++)
                {
                    var phase = (t * 0.6f + i / 3f) % 1f;
                    var rx = d.BodyLen * (0.45 + phase * 0.9);
                    var a = (byte)(150 * (1 - phase));
                    ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(a, 255, 255, 255)), 1.4), new Point(o.X, o.Y), rx, rx * SinP * 0.9);
                }
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), null, new Point(o.X, o.Y), d.BodyLen * 0.45, d.BodyLen * 0.45 * SinP * 0.9);
                break;
            }
            case "shake":
            {
                var brush = new SolidColorBrush(Color.FromArgb(170, 0x6F, 0xB7, 0xFF));
                for (var i = 0; i < 10; i++)
                {
                    var a = i * 0.628 + t * 3;
                    var rad = d.BodyLen * 0.5 + ((t * 2.5f + i * 0.1f) % 1f) * d.BodyLen * 0.6;
                    ctx.DrawEllipse(brush, null, new Point(o.X + Math.Cos(a) * rad, o.Y - d.ShoulderH * 0.7 + Math.Sin(a) * rad * 0.6), 2.2, 2.2);
                }
                break;
            }
            case "dig":
            {
                var brush = new SolidColorBrush(Color.FromArgb(200, 0x7A, 0x5A, 0x3A));
                var back = cam.Project(new V3(-1, 0, 0));
                for (var i = 0; i < 6; i++)
                {
                    var phase = (t * 1.6f + i * 0.17f) % 1f;
                    var dist = phase * d.BodyLen * 0.9;
                    var x = o.X + back.X * dist;
                    var y = o.Y + back.Y * dist - 4 - Math.Sin(phase * Math.PI) * 26;
                    ctx.DrawEllipse(brush, null, new Point(x, y), 2.5, 2.2);
                }
                break;
            }
            case "petted":
            {
                var brush = new SolidColorBrush(Color.FromArgb(190, 0xFF, 0x6B, 0x8A));
                var headTop = o.Y - (d.ShoulderH + d.Neck + d.HeadR * 1.6) * CosP;
                for (var i = 0; i < 2; i++)
                {
                    var phase = (t * 0.7f + i * 0.5f) % 1f;
                    var x = o.X + 12 + i * 12;
                    var y = headTop - phase * 28;
                    var size = 3.5 * (1 - phase * 0.5);
                    ctx.DrawEllipse(brush, null, new Point(x - size * 0.5, y), size, size);
                    ctx.DrawEllipse(brush, null, new Point(x + size * 0.5, y), size, size);
                    var g = new StreamGeometry();
                    using (var gc = g.Open())
                    {
                        gc.BeginFigure(new Point(x - size, y + size * 0.3), true);
                        gc.LineTo(new Point(x, y + size * 1.7));
                        gc.LineTo(new Point(x + size, y + size * 0.3));
                        gc.EndFigure(true);
                    }
                    ctx.DrawGeometry(brush, null, g);
                }
                break;
            }
        }
    }

    // ------------------------------------------------------------------ colors

    private static Style StyleOf(Pet pet)
    {
        var w = pet.Appearance.Wet;
        Color Wet(byte r, byte g, byte b) => Darken(Color.FromRgb(r, g, b), 0.35 * w);
        return pet.Species == PetSpecies.Cat
            // Kitten, orange tabby: warm orange coat with darker stripes, cream muzzle, chest and paws, big brown eyes.
            ? new Style(
                Fur: Wet(0xF7, 0xB4, 0x62), Belly: Wet(0xFD, 0xE8, 0xC2), Dark: Color.FromRgb(0x86, 0x48, 0x1E),
                Nose: Color.FromRgb(0x9C, 0x5A, 0x48), EarInner: Color.FromRgb(0xF4, 0xA6, 0x8A), Paw: Wet(0xFD, 0xE8, 0xC2),
                Saddle: Wet(0xE6, 0x8A, 0x34), Chest: Wet(0xFD, 0xE6, 0xBC), Leg: Wet(0xF7, 0xB4, 0x62),
                Iris: Color.FromRgb(0x6E, 0x38, 0x1C))
            // Pomeranian, orange sable: orange coat with darker tipping on the back, cream ruff, chest and legs.
            : new Style(
                Fur: Wet(0xEA, 0x95, 0x3C), Belly: Wet(0xFB, 0xE6, 0xC0), Dark: Color.FromRgb(0x5A, 0x34, 0x14),
                Nose: Color.FromRgb(0x1E, 0x16, 0x12), EarInner: Color.FromRgb(0xF2, 0xB8, 0x88), Paw: Wet(0xF4, 0xD4, 0xA0),
                Saddle: Wet(0xB4, 0x5E, 0x20), Chest: Wet(0xFC, 0xE6, 0xBE), Leg: Wet(0xF2, 0xC6, 0x86),
                Iris: Color.FromRgb(0x3A, 0x22, 0x14));
    }

    private static Color MudColor(Pet pet) => pet.Appearance.MudDried ? Color.FromRgb(0x8A, 0x6A, 0x4E) : Color.FromRgb(0x5B, 0x3A, 0x1E);

    private static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
