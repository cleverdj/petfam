using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;

namespace PetFam.Desktop.Rendering;

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

    /// <summary>
    /// Body proportions for this pet's age (plan §4.6): a baby is small with a big head, short legs and a short
    /// body; a junior is lanky; an adult has the full proportions. Changes a little every day in between.
    /// </summary>
    public static Dims DimsFor(Pet pet)
    {
        var a0 = DimsOf(pet.Species);
        var look = BreedLook.Of(pet);
        // The breed's build: a Corgi's long body on short legs, a British Shorthair's round head, ...
        // Size: a Golden Retriever or a Maine Coon stands a head taller than a Chihuahua. Heads grow less than bodies
        // (a big dog has a relatively smaller head), so the big breeds don't just look like a zoomed-in puppy.
        var z = look.Size;
        var a = new Dims(a0.BodyLen * look.BodyLen * z, a0.BodyR * look.BodyR * z, a0.ShoulderH * look.Legs * z, a0.HipH * look.Legs * z,
            a0.L1 * look.Legs * z, a0.L2 * look.Legs * z, a0.HeadR * look.Head * (0.35f + 0.65f * z), a0.Neck * z,
            a0.Muzzle * (look.Face == FaceKind.Fox ? 1.6f : 1f) * look.Snout);
        var g = pet.Growth.BodyGrowth;
        const float j = Growth.JuniorAt / (float)Growth.AdultAt;
        // Body length, body radius, legs, head, neck, muzzle, relative to the adult.
        float[] baby = { 0.62f, 0.74f, 0.6f, 0.82f, 0.6f, 0.7f };
        float[] junior = { 0.86f, 0.82f, 0.95f, 0.9f, 0.9f, 0.9f };
        float[] adult = { 1f, 1f, 1f, 1f, 1f, 1f };
        var (from, to, t) = g < j ? (baby, junior, g / j) : (junior, adult, (g - j) / (1 - j));
        var k = Enumerable.Range(0, 6).Select(i => from[i] + (to[i] - from[i]) * t).ToArray();
        return new Dims(a.BodyLen * k[0], a.BodyR * k[1], a.ShoulderH * k[2], a.HipH * k[2], a.L1 * k[2], a.L2 * k[2],
            a.HeadR * k[3], a.Neck * k[4], a.Muzzle * k[5]);
    }

    /// <summary>Fur displacement amplitude (model units) used by the 3D shader.</summary>
    public static float FurAmount(PetSpecies s) => s == PetSpecies.Cat ? 1.1f : 1.5f;

    /// <summary>
    /// Cartoon look (default): cel shading in flat bands, brown outlines, big glossy cartoon eyes, no fur noise.
    /// PETFAM_STYLE=fur switches back to the soft fur look.
    /// </summary>
    public static bool Toon { get; } = Environment.GetEnvironmentVariable("PETFAM_STYLE") != "fur";

    /// <summary>Cel-shading tint of the shadow bands: cool for the grey cat, warm for the orange dog.</summary>
    public static (float R, float G, float B) ShadeTint(PetSpecies s) => s == PetSpecies.Cat
        ? (0.90f, 0.92f, 0.98f)
        : (0.98f, 0.86f, 0.80f);

    /// <summary>Colour multiplier of the tabby stripes: charcoal on the grey cat.</summary>
    public static (float R, float G, float B) StripeTint(PetSpecies s) => s == PetSpecies.Cat
        ? (0.58f, 0.60f, 0.66f)
        : (0.88f, 0.66f, 0.46f);

    public static (float R, float G, float B) ShadeTint(Pet pet) => BreedLook.Of(pet).Shade;

    public static (float R, float G, float B) StripeTint(Pet pet) => BreedLook.Of(pet).StripeTint;

    public static (float R, float G, float B) LineColor(Pet pet)
    {
        var c = BreedLook.Of(pet).Line;
        return ((c >> 16 & 0xFF) / 255f, (c >> 8 & 0xFF) / 255f, (c & 0xFF) / 255f);
    }

    /// <summary>Outline colour of the cartoon style.</summary>
    public static (float R, float G, float B) LineColor(PetSpecies s) => s == PetSpecies.Cat
        ? (0x45 / 255f, 0x4A / 255f, 0x55 / 255f)
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

    /// <summary>Only the body (no bubbles or "z z").</summary>
    public static void DrawBodyOnly(DrawingContext ctx, Pet pet, Vec2 origin, float depth) => DrawBody(ctx, pet, origin, depth, overlays: false);

    public static void DrawBody(DrawingContext ctx, Pet pet, Vec2 origin, float depth, bool overlays = true)
    {
        var d = DimsFor(pet);
        var style = StyleOf(pet);
        var t = pet.AnimationTime;
        var cam = new Camera(pet.Heading);
        var scene = new Scene(cam);
        var o = new Point(origin.X, origin.Y);

        // Look target in screen pixels relative to the feet (depth 1).
        Vec2? look = pet.LookAt is { } la ? new Vec2((la.X - pet.Position.X) / depth, (la.Y - pet.Position.Y) / depth) : null;

        var pose = AnimatedPose(pet, d, cam, look, depth);

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
        RememberHead(pet, cam, pose, scene);
        var sx = Math.Abs(Math.Cos(yaw)) * pose.SqF + Math.Abs(Math.Sin(yaw)) * pose.SqZ;
        using (ctx.PushTransform(Matrix.CreateTranslation(-o.X, -o.Y) * Matrix.CreateScale(sx, pose.SqY) * Matrix.CreateTranslation(o.X, o.Y)))
            scene.Flush(ctx, o);

        if (overlays) DrawOverlays(ctx, pet, origin);
    }

    /// <summary>
    /// Builds the SDF primitive list for the GPU renderer (world space, feet at the origin, depth-1 units).
    /// Smooth primitives come first, hard ones (eyes, nose) last, as the shader expects.
    /// </summary>
    /// <summary>SDF primitives plus projected whisker lines (x1,y1,x2,y2,alpha relative to the feet).</summary>
    public sealed record SdfBuild(List<SdfPrim> Prims, List<float> Whiskers, float Fur, float WhiskerWidth, float[] Squash)
    {
        /// <summary>Curly coat bump height in model units (0: smooth).</summary>
        public float Curl { get; init; }
    }

    public static SdfBuild BuildSdf(Pet pet, float depth)
    {
        var d = DimsFor(pet);
        var style = StyleOf(pet);
        var cam = new Camera(pet.Heading);
        var scene = new Scene(cam);
        Vec2? look = pet.LookAt is { } la ? new Vec2((la.X - pet.Position.X) / depth, (la.Y - pet.Position.Y) / depth) : null;
        var pose = AnimatedPose(pet, d, cam, look, depth);
        BuildScene(scene, pet, d, style, pose, pet.AnimationTime);
        RememberHead(pet, cam, pose, scene);

        var list = new List<SdfPrim>(scene.Sdf.Count);
        list.AddRange(scene.Sdf.Where(p => p.K > 0));
        list.AddRange(scene.Sdf.Where(p => p.K <= 0));

        // Whiskers: hidden when the muzzle points away, faint on the far side of the face.
        var whiskers = new List<float>();
        var headZ = cam.World(Squashed(pose, scene.HeadCenter)).zr;
        foreach (var (a0, b0) in scene.Whiskers)
        {
            var a = Squashed(pose, a0);
            var b = Squashed(pose, b0);
            var za = cam.World(a).zr;
            if (za < headZ - d.HeadR * 0.6) continue;
            var alpha = za < headZ - d.HeadR * 0.1 ? 0.35f : 0.9f;
            var pa = cam.Project(a);
            var pb = cam.Project(b);
            whiskers.AddRange(new[] { (float)pa.X, (float)pa.Y, (float)pb.X, (float)pb.Y, alpha });
        }

        return new SdfBuild(list, whiskers, Toon ? 0f : FurAmount(pet.Species), pet.Species == PetSpecies.Cat ? 0.55f : 0.45f,
            new[] { (float)pose.SqF, (float)pose.SqY, (float)pose.SqZ })
        {
            Curl = BreedLook.Of(pet).Coat == CoatKind.Curly ? 1.4f * d.BodyR / 14f : 0f,
        };
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Pet, StrongBox<Point>> HeadSpots = new();

    private static void RememberHead(Pet pet, Camera cam, Pose pose, Scene scene)
    {
        var p = cam.Project(Squashed(pose, scene.HeadCenter));
        HeadSpots.GetValue(pet, _ => new StrongBox<Point>()).Value = new Point(p.X, p.Y);
    }

    /// <summary>Centre of the head relative to the feet (model units, depth 1), as last drawn.</summary>
    public static Point HeadSpot(Pet pet)
    {
        if (HeadSpots.TryGetValue(pet, out var box) && box.Value != default) return box.Value;
        var d = DimsFor(pet);
        return new Point(d.BodyLen * 0.3, -(d.ShoulderH + d.Neck + d.HeadR * 1.3) * CosP);
    }

    /// <summary>Head top above the feet (model units, depth 1) when standing, for fitting a pet through a door.</summary>
    public static double StandingHeight(Pet pet)
    {
        var d = DimsFor(pet);
        return (d.ShoulderH + d.Neck + d.HeadR * 2.2) * CosP;
    }

    public static float CameraSin => (float)SinP;
    public static float CameraCos => (float)CosP;

    /// <summary>2D effects drawn over the body: water drops, dirt, hearts, "z z".</summary>
    /// <summary>Head top above the feet (sprite units, depth 1): where the plumbob and the speech bubble go.</summary>
    public static double HeadTopOffset(Pet pet)
    {
        var d = DimsFor(pet);
        return pet.Animation is "sleep" or "lie" ? d.HeadR * 2.6 * CosP : (d.ShoulderH + d.Neck + d.HeadR * 2.9) * CosP;
    }

    /// <param name="bubble">False: the caller draws the speech bubble itself (at a fixed size, e.g. in a scaled-up preview).</param>
    public static void DrawOverlays(DrawingContext ctx, Pet pet, Vec2 origin, bool bubble = true)
    {
        var d = DimsFor(pet);
        var t = pet.AnimationTime;
        var o = new Point(origin.X, origin.Y);
        DrawParticles(ctx, pet, d, o, t, new Camera(pet.Heading));
        DrawCartoonFx(ctx, pet, d, o);

        if (pet.Animation is "sleep" or "doze")
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
        if (bubble && pet.Bubble is { } said) DrawBubble(ctx, new Point(o.X, headTop - (pet.Selected ? 40 : 10)), said, pet.BubbleTimer);
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

    /// <summary>
    /// Loads the speech-bubble font and lays out a line of each script on a worker thread at start-up: the first
    /// bubble used to stall the screen for about two seconds while the (large, Korean-capable) font loaded.
    /// </summary>
    public static void PrewarmFonts() => Task.Run(() =>
    {
        var sw = Stopwatch.StartNew();
        try
        {
            foreach (var size in new[] { 12.5, 13.0 })
            {
                var ft = new FormattedText("Hello! Woof, meow? 안녕! 냥냥 멍멍 …·°C", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    BubbleFont, size, Brushes.Black) { MaxTextWidth = 180 };
                _ = ft.Height + ft.WidthIncludingTrailingWhitespace;
            }
            AppLog.Write($"Fonts ready in {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            AppLog.Write("Font prewarm failed: " + ex.Message);
        }
    });

    /// <summary>
    /// A speech bubble whose tail points at <paramref name="tip"/>. Long lines wrap at <paramref name="maxWidth"/>;
    /// with <paramref name="within"/> the bubble is kept inside that area (its tail still points at the pet).
    /// </summary>
    public static void DrawBubble(DrawingContext ctx, Point tip, string text, float timeLeft,
        double maxWidth = 200, double fontSize = 13, Rect? within = null)
    {
        var alpha = (byte)(255 * Math.Clamp(timeLeft / 0.3, 0, 1));
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, BubbleFont, fontSize,
            new SolidColorBrush(Color.FromArgb(alpha, 0x1C, 0x2A, 0x40)))
        {
            MaxTextWidth = Math.Max(40, maxWidth - 16),
            TextAlignment = TextAlignment.Center,
        };
        var w = Math.Min(maxWidth, ft.WidthIncludingTrailingWhitespace + 16);
        var h = ft.Height + 8;
        var rect = new Rect(tip.X - w / 2, tip.Y - h - 7, w, h);
        if (within is { } area)
        {
            var x = Math.Clamp(rect.X, area.X + 2, Math.Max(area.X + 2, area.Right - w - 2));
            var y = Math.Max(rect.Y, area.Y + 2);
            rect = new Rect(x, y, w, h);
            tip = new Point(Math.Clamp(tip.X, rect.X + 10, rect.Right - 10), Math.Max(tip.Y, rect.Bottom + 5));
        }
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
        // Centred text: the layout box is MaxTextWidth wide, so place it so its middle is the bubble's middle.
        ctx.DrawText(ft, new Point(rect.X + rect.Width / 2 - ft.MaxTextWidth / 2, rect.Y + 4));
    }

    private static V3 Cross(V3 a, V3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static RectI HitBox(Pet pet, float depth)
    {
        var d = DimsFor(pet);
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
        var travel = R * 2.1f * s.Progress;
        return s.Edge switch
        {
            PeekEdge.Top => (new Point(s.Anchor.X, r.Top + R - travel), new Vector(0, -1)),
            PeekEdge.Bottom => (new Point(s.Anchor.X, r.Bottom - R + travel), new Vector(0, 1)),
            PeekEdge.Left => (new Point(r.Left + R - travel, s.Anchor.Y), new Vector(-1, 0)),
            _ => (new Point(r.Right - R + travel, s.Anchor.Y), new Vector(1, 0)),
        };
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

        /// <summary>0 standing … 1 hind legs tucked. Set from HindFolded, blended during transitions.</summary>
        public double Fold;

        /// <summary>Cartoon squash and stretch: scale along the body (F), up (Y) and across (Z), anchored at the feet.</summary>
        public double SqF = 1, SqY = 1, SqZ = 1;

        /// <summary>Tail centre line from root to tip, filled in by BuildPose.</summary>
        public List<V3> Tail = new();

        public Pose Clone()
        {
            var c = (Pose)MemberwiseClone();
            c.Feet = (V3?[])Feet.Clone();
            c.Knees = (V3?[])Knees.Clone();
            c.Tail = new List<V3>(Tail);
            return c;
        }
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
        var walked = pet.Motion.Distance;
        double? Cycles(bool run) => walked > 0 ? walked / (4 * StrideOf(d, run)) : null;
        var pose = anim switch
        {
            "walk" => GaitPose(d, t, run: false, Cycles(false)),
            "run" => GaitPose(d, t, run: true, Cycles(true)),
            "sit" or "peek" => SitPose(d, t),
            "pant" => PantPose(d, t),
            "yawn" => YawnPose(d, t),
            "sniff" => SniffPose(d, t),
            "knead" => KneadPose(d, t),
            "scratch" => ScratchPose(d, t),   // peeking: sitting behind the window, tail tucked out of sight
            "groom" => GroomPose(d, t),
            "petted" => PettedPose(pet, d, t),
            "lie" => LiePose(d, t),
            "doze" => DozePose(d, t),
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

        if (pet.Animation is not ("eat" or "dig" or "stretch" or "sleep" or "groom" or "sniff"))
            pose.Head = pose.Head + new V3(d.HeadR * 0.25, d.HeadR * 0.4, 0);


        pose.Fold = pose.HindFolded ? 1 : 0;
        if (!pose.CurledSleep) pose.Tail = TailPoints(pet.Species, d, pose, t);

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
        for (var i = 0; i < p.Tail.Count; i++) p.Tail[i] = Up(p.Tail[i]);
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

    /// <summary>Sitting and swiping with one front paw (the cat punch).</summary>
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

    /// <summary>Half the foot travel of one step. The body moves four of these per gait cycle.</summary>
    private static double StrideOf(Dims d, bool run) => d.BodyLen * (run ? 0.38 : 0.25);

    /// <param name="cycles">Gait cycles completed, from the distance walked; null runs the cycle on the clock.</param>
    private static Pose GaitPose(Dims d, float t, bool run, double? cycles = null)
    {
        var rate = run ? 2.4 : 1.4;
        var phase = cycles ?? t * rate;
        var stride = StrideOf(d, run);
        var lift = run ? 11.0 : 5.0;
        var bob = run ? Math.Sin(phase * Math.PI * 2) * 3.2 : Math.Sin(phase * Math.PI * 4) * 1.1;
        var p = Standing(d, bob);

        // Walk: lateral sequence LH, LF, RH, RF. Trot: diagonal pairs.
        double[] ph = run ? new[] { 0.0, 0.5, 0.5, 0.0 } : new[] { 0.75, 0.25, 0.5, 0.0 };
        for (var i = 0; i < 4; i++)
        {
            // Stance (first half): the foot is planted and slides back under the body at exactly body speed.
            // Swing (second half): it lifts and eases forward to the next footfall.
            var rest = p.Feet[i]!.Value;
            var u = phase + ph[i];
            u -= Math.Floor(u);
            double x, y;
            if (u < 0.5)
            {
                x = stride * (1 - 4 * u);
                y = 0;
            }
            else
            {
                var v = (u - 0.5) * 2;
                x = -stride + 2 * stride * (0.5 - 0.5 * Math.Cos(Math.PI * v));
                y = lift * Math.Sin(Math.PI * v);
            }
            p.Feet[i] = new V3(rest.X + x, y, rest.Z);
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

    /// <summary>Sitting after a sprint: mouth open, tongue out, quick shallow breaths.</summary>
    private static Pose PantPose(Dims d, float t)
    {
        var p = SitPose(d, t);
        var breath = Math.Sin(t * 14) * 0.6;
        p.Shoulder = p.Shoulder + new V3(0, breath, 0);
        p.Head = p.Head + new V3(0, breath * 0.5, 0);
        p.Mouth = 0.55;
        p.Tongue = true;
        p.TailWag = 2.2;
        return p;
    }

    /// <summary>A big yawn: head tipped up, mouth opening wide and closing again, eyes shut at the peak.</summary>
    private static Pose YawnPose(Dims d, float t)
    {
        var p = SitPose(d, t);
        var k = Math.Clamp(t / 1.6, 0, 1);
        var open = Math.Sin(k * Math.PI);                       // 0 → 1 → 0 over the yawn
        p.Mouth = open;
        p.HeadPitch = -18 * open;
        if (open > 0.35) p.Eyes = Eye.Closed;
        p.EarBack = 0.3 * open;
        return p;
    }

    /// <summary>Kneading ("making biscuits"): front paws pressing down in turn, chest low, eyes shut in bliss.</summary>
    private static Pose KneadPose(Dims d, float t)
    {
        var p = SitPose(d, t);
        p.Shoulder = p.Shoulder + new V3(2, -d.ShoulderH * 0.12, 0);
        var zs = d.BodyR * 0.55;
        var beat = t * 5.0;
        var right = Math.Max(0, Math.Sin(beat)) * d.L1 * 0.32;
        var left = Math.Max(0, -Math.Sin(beat)) * d.L1 * 0.32;
        p.Feet[0] = new V3(p.Shoulder.X + 5 + right * 0.4, right, zs);
        p.Feet[1] = new V3(p.Shoulder.X + 5 + left * 0.4, left, -zs);
        p.Head = p.Head + new V3(0, -d.ShoulderH * 0.08 + Math.Sin(beat * 2) * 0.4, 0);
        p.HeadPitch = 6;
        p.Eyes = Eye.Happy;
        p.TailWag = 0.5;
        return p;
    }

    /// <summary>Sitting and scratching behind the ear with a hind foot, fast, head tipped into it, eyes shut.</summary>
    private static Pose ScratchPose(Dims d, float t)
    {
        var p = SitPose(d, t);
        var zh = d.BodyR * 0.75;
        var rub = Math.Sin(t * 22) * 2.5;
        // Out to the side, so the leg reads past the round body: knee forward, paw up at the ear.
        p.Knees[2] = new V3(p.Hip.X + d.L1 * 0.9, p.Hip.Y + d.L1 * 0.35, zh * 1.9);
        p.Feet[2] = new V3(p.Shoulder.X + rub * 0.4, p.Head.Y - d.HeadR * 0.15 + rub, zh * 1.7);
        p.Head = p.Head + new V3(-1, -2, 0);
        p.HeadYaw = 18;
        p.HeadPitch = 10;
        p.Eyes = Eye.Closed;
        p.Mouth = 0.35;
        p.Tongue = true;
        p.EarBack = 0.25;
        p.TailWag = 2.5;
        return p;
    }

    /// <summary>A butterfly fluttering at <paramref name="at"/>: two pairs of wings flapping, a little body.</summary>
    public static void DrawButterfly(DrawingContext ctx, Point at, double time, double size = 1.0)
    {
        var flap = Math.Abs(Math.Sin(time * 16));
        var w = (3 + 7 * flap) * size;
        var h = 7 * size;
        var wing = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x3A));
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x6A, 0x3A, 0x10)), 1);
        var low = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x3A));
        ctx.DrawEllipse(low, edge, new Point(at.X - w * 0.55, at.Y + h * 0.45), w * 0.5, h * 0.4);
        ctx.DrawEllipse(low, edge, new Point(at.X + w * 0.55, at.Y + h * 0.45), w * 0.5, h * 0.4);
        ctx.DrawEllipse(wing, edge, new Point(at.X - w * 0.6, at.Y - h * 0.25), w * 0.6, h * 0.55);
        ctx.DrawEllipse(wing, edge, new Point(at.X + w * 0.6, at.Y - h * 0.25), w * 0.6, h * 0.55);
        ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x26, 0x14)), null, new Rect(at.X - 1, at.Y - h * 0.7, 2, h * 1.4), 1, 1);
    }

    /// <summary>Nose to the ground, working along the scent from side to side.</summary>
    private static Pose SniffPose(Dims d, float t)
    {
        var p = EatPose(d, t);
        p.HeadYaw = Math.Sin(t * 3.2) * 18;
        p.Mouth = 0;
        p.TailWag = 1.6;
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

    /// <summary>Lying down with the head up and eyes closed: napping in the house doorway, face showing.</summary>
    private static Pose DozePose(Dims d, float t)
    {
        var p = LiePose(d, t * 0.7f);
        p.Eyes = Eye.Closed;
        p.HeadPitch = 10;
        p.Head = p.Head - new V3(0, d.HeadR * 0.15, 0);
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

        if (look is not { } l || anim is not ("idle" or "walk" or "sit" or "lie" or "petted" or "run" or "pant"))
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

    // ------------------------------------------------------------------ animator

    /// <summary>Per-pet animation memory: the pose being blended away from, smoothed look and motion.</summary>
    private sealed class AnimState
    {
        public string Clip = "";
        public Pose? Last, From;
        public double Blend = 1, BlendTime = 0.25;
        public long Ticks;
        public bool Started;
        public double LookYaw, LookPitch, Turn, Accel, Clock;
        public string PrevClip = "";

        // Cartoon layer: squash springs, secondary motion, effects.
        public double SqY, SqYV, SqF, SqFV;
        public double HeadX, HeadXV, HeadY, HeadYV, TailX, TailXV, TailY, TailYV;
        public double PrevLift, LiftVel, Peak, Still, StillBefore, Time;
        public long StepCount;
        public float Depth = 1;
        public Point HeadScreen;
        public Random? Rng;
        public readonly List<Fx> Fx = new();
        public Emote Emote;
        public double EmoteAge = 99;
    }

    private static readonly ConditionalWeakTable<Pet, AnimState> States = new();

    /// <summary>Frame time override for offline rendering (the motion filmstrip); null uses the wall clock.</summary>
    internal static double? FixedDt { get; set; }

    /// <summary>
    /// The pose to draw this frame, the way a game animator would produce it.
    /// Clip changes crossfade from the last drawn pose instead of snapping.
    /// The head follows a look target smoothly, leads into turns, and the spine bends with the turn.
    /// The tail swings out and lags, and the body pitches forward when speeding up and back when braking.
    /// </summary>
    private static Pose AnimatedPose(Pet pet, Dims d, Camera cam, Vec2? look, float depth)
    {
        var pose = BuildPose(pet, d, pet.AnimationTime);
        if (pet.Growth.Seniority > 0 && !pose.CurledSleep) pose.Head -= new V3(0, d.HeadR * 0.16 * pet.Growth.Seniority, 0);
        var (yaw0, pitch0) = (pose.HeadYaw, pose.HeadPitch);
        ApplyLook(pose, cam, d, look, pet);
        var lookYaw = pose.HeadYaw - yaw0;
        var lookPitch = pose.HeadPitch - pitch0;
        pose.HeadYaw = yaw0;
        pose.HeadPitch = pitch0;

        var st = States.GetValue(pet, _ => new AnimState());
        var now = Stopwatch.GetTimestamp();
        var dt = !st.Started ? 0 : FixedDt ?? Math.Clamp((now - st.Ticks) / (double)Stopwatch.Frequency, 0, 0.1);
        st.Ticks = now;

        // Crossfade between clips.
        string? changedFrom = null;
        if (pet.Animation != st.Clip)
        {
            if (st.Started) changedFrom = st.Clip;
            st.PrevClip = st.Clip;
            if (st.Started && st.Last is { CurledSleep: false } last && !pose.CurledSleep)
            {
                st.From = last;
                st.Blend = 0;
                st.BlendTime = BlendTime(st.Clip, pet.Animation);
            }
            st.Clip = pet.Animation;
        }
        if (st.From is { } from && st.Blend < 1 && !pose.CurledSleep)
        {
            st.Blend = Math.Min(1, st.Blend + dt / st.BlendTime);
            var w = st.Blend * st.Blend * (3 - 2 * st.Blend);
            // The body overshoots the new pose and settles back (feet stay on the smooth curve so they don't skate).
            var gait = st.PrevClip is "walk" or "run" && st.Clip is "walk" or "run";
            var wb = gait ? w : EaseOutBack(st.Blend, st.BlendTime <= 0.1 ? 1.6 : 1.1);
            pose = BlendPoses(from, pose, w, d, wb);
        }
        st.Last = pose.Clone();

        // Blink on the pet's own clock, so blinks don't line up with every clip change.
        st.Clock += dt;
        var blinkAt = (st.Clock + Math.Abs(pet.Id.GetHashCode() % 97) * 0.1) % 4.3;
        if (pose.Eyes == Eye.Open && (blinkAt < 0.12 || (pet.Id.GetHashCode() & 1) == 0 && blinkAt is > 0.3 and < 0.4))
            pose.Eyes = Eye.Closed;

        // Smoothed look: the head swings to a new target instead of jumping there.
        if (!st.Started)
        {
            st.LookYaw = lookYaw;
            st.LookPitch = lookPitch;
            st.Started = true;
        }
        var kLook = 1 - Math.Exp(-dt * 8);
        st.LookYaw += (lookYaw - st.LookYaw) * kLook;
        st.LookPitch += (lookPitch - st.LookPitch) * kLook;
        pose.HeadYaw += st.LookYaw;
        pose.HeadPitch += st.LookPitch;

        if (pose.CurledSleep || pet.Hold == HoldState.Carried)
        {
            Cartoon(pet, d, cam, st, pose, dt, changedFrom, depth);
            return pose;
        }

        // Motion follow-through from the measured movement. Heading increasing turns toward +z (the pet's right).
        var m = pet.Motion;
        var turn = Math.Clamp(m.TurnRate / 420.0, -1, 1);
        var accel = Math.Clamp(m.Acceleration / 380.0, -1, 1);
        st.Turn += (turn - st.Turn) * (1 - Math.Exp(-dt * 7));
        st.Accel += (accel - st.Accel) * (1 - Math.Exp(-dt * 6));

        var bend = st.Turn * d.BodyR;
        pose.HeadYaw += st.Turn * 30;
        pose.Head += new V3(0, 0, bend * 0.55);
        pose.Shoulder += new V3(0, 0, bend * 0.3);
        pose.Hip -= new V3(0, 0, bend * 0.12);
        for (var i = 1; i < pose.Tail.Count; i++)
            pose.Tail[i] -= new V3(0, 0, bend * 0.45 * i);

        var lean = st.Accel * (1 - pose.Fold);
        pose.Shoulder -= new V3(0, lean * 1.6, 0);
        pose.Hip += new V3(0, lean * 0.6, 0);
        pose.Head += new V3(lean * 1.8, -lean * 1.4, 0);
        for (var i = 1; i < pose.Tail.Count; i++)
            pose.Tail[i] -= new V3(0, lean * 1.2 * i, 0);
        if (Toon) Cartoon(pet, d, cam, st, pose, dt, changedFrom, depth);
        return pose;
    }

    // ------------------------------------------------------------------ cartoon layer (2D animation feel)

    private enum FxKind { Dust, Star, Confetti }

    private enum Emote { None, Bang, Anger, Note, Sweat }

    private sealed class Fx
    {
        public FxKind Kind;
        public double X, Y, Vx, Vy, Age, Life, Size;
        public int Seed;
    }

    /// <summary>Damped spring step toward target; low damping overshoots and wobbles like hand-drawn jelly.</summary>
    private static void Spring(ref double x, ref double v, double target, double hz, double zeta, double dt)
    {
        if (dt <= 0) return;
        var w = 2 * Math.PI * hz;
        var n = Math.Max(1, (int)Math.Ceiling(dt * 240));
        var h = dt / n;
        for (var i = 0; i < n; i++)
        {
            v += (-w * w * (x - target) - 2 * zeta * w * v) * h;
            x += v * h;
        }
    }

    private static double EaseOutBack(double x, double c)
    {
        var u = x - 1;
        return 1 + (c + 1) * u * u * u + c * u * u;
    }

    private static bool Settles(string clip) => clip is "sit" or "lie" or "sleep" or "eat" or "groom";

    /// <summary>
    /// Classic 2D animation principles on top of the 3D rig:
    /// squash and stretch (footfalls, hops, landings, starts and stops) on a jelly spring that overshoots,
    /// anticipation before a dash, a head that bobbles and a tail that whips behind the body,
    /// plus cartoon effects: dust puffs, impact stars and emote marks.
    /// </summary>
    private static void Cartoon(Pet pet, Dims d, Camera cam, AnimState st, Pose pose, double dt, string? changedFrom, float depth)
    {
        var m = pet.Motion;
        var anim = pet.Animation;
        st.Time += dt;
        st.Depth = depth;
        st.Rng ??= new Random(pet.Id.GetHashCode());
        var free = !pose.CurledSleep && pet.Hold != HoldState.Carried;

        // Hops: vertical speed, take-off and landing.
        var lift = (double)pet.Lift;
        if (dt > 0) st.LiftVel += ((lift - st.PrevLift) / dt - st.LiftVel) * (1 - Math.Exp(-dt * 25));
        var landed = st.PrevLift > 1.5 && lift <= 0.5;
        var tookOff = st.PrevLift <= 0.5 && lift > 1.5;
        var impact = Math.Clamp(Math.Abs(st.LiftVel) / 250, 0.4, 1.5);
        st.PrevLift = lift;

        var speedN = Math.Clamp(m.GroundSpeed / 140.0, 0, 1.2);
        double ty = 0, tf = 0;
        if (anim == "run") tf += 0.06 * speedN;
        if (lift > 0.5)
        {
            var s = Math.Clamp(Math.Abs(st.LiftVel) / 260, 0, 1) * 0.16;
            ty += s;
            tf -= s * 0.3;
        }
        if (pet.Hold == HoldState.Carried) ty += 0.07;

        if (landed && free)
        {
            st.SqYV -= 1.5 * impact;
            st.SqFV += 0.5 * impact;
            Puff(pet, cam, st, d.BodyLen * 0.1, 3, 1.1 * impact, spread: true);
            if (anim is "pounce" or "wrestle" or "box") Star(pet, cam, st, d.BodyLen * 0.75, impact);
        }
        if (tookOff) st.SqYV += 1.1;

        if (changedFrom is { } from && free)
        {
            switch (anim)
            {
                case "startle": st.SqYV += 2.6; SetEmote(st, Emote.Bang); break;
                case "hiss": st.SqYV += 1.2; SetEmote(st, Emote.Anger); break;
                case "playbow": st.SqYV -= 1.1; SetEmote(st, Emote.Note); break;
                case "submit": st.SqYV -= 0.9; SetEmote(st, Emote.Sweat); break;
                case "stalk" when from is not ("wiggle" or "pounce"): SetEmote(st, Emote.Bang); break;
                case "pounce": st.SqFV += 1.3; break;
                case "petted": st.SqYV -= 0.8; break;
                case "run" when from != "walk":
                    st.SqFV -= 1.0;
                    Puff(pet, cam, st, -d.BodyLen * 0.45, 2, 0.9, spread: false);
                    break;
            }
            if (Settles(anim) && !Settles(from)) st.SqYV -= 1.2;
            if (Settles(from) && !Settles(anim)) st.SqYV += 0.9;
        }

        // Starts and hard stops read from the measured speed.
        st.Peak = Math.Max(st.Peak * Math.Exp(-dt * 1.5), m.GroundSpeed);
        st.Still = m.GroundSpeed < 8 ? st.Still + dt : 0;
        if (free && m.GroundSpeed < 15 && st.Peak > 75)
        {
            st.SqFV -= 1.1 * Math.Min(1.3, st.Peak / 120);
            Puff(pet, cam, st, d.BodyLen * 0.35, 2, 0.8, spread: true);
            st.Peak = 0;
        }
        if (free && m.GroundSpeed > 25 && st.StillBefore > 0.35)
        {
            st.SqFV -= 0.7;
            st.SqYV -= 0.5;
            if (anim == "run") Puff(pet, cam, st, -d.BodyLen * 0.45, 2, 0.8, spread: false);
        }
        st.StillBefore = m.GroundSpeed > 25 ? 0 : Math.Max(st.StillBefore, st.Still);

        Spring(ref st.SqY, ref st.SqYV, ty, 3.2, 0.3, dt);
        Spring(ref st.SqF, ref st.SqFV, tf, 2.6, 0.28, dt);

        // Footfall bounce: a light squash on each step, a big gallop squash and stretch when running.
        double gy = 0, gf = 0;
        if (anim == "walk")
        {
            var ph = m.Distance / (4 * StrideOf(d, false));
            gy = 0.028 * Math.Min(1, speedN * 1.6) * Math.Sin(ph * Math.PI * 4);
        }
        else if (anim == "run")
        {
            var ph = m.Distance / (4 * StrideOf(d, true));
            var s = Math.Sin(ph * Math.PI * 2);
            var k = Math.Min(1, speedN);
            gy = 0.075 * k * s;
            gf = -0.045 * k * s;
            var half = (long)Math.Floor(ph * 2);
            if (half != st.StepCount && speedN > 0.6 && st.Rng.NextDouble() < 0.6)
                Puff(pet, cam, st, -d.BodyLen * 0.4, 1, 0.6, spread: false);
            st.StepCount = half;
        }
        if (Settles(anim) || anim is "idle" || pose.CurledSleep)
            gy += 0.016 * Math.Sin(st.Time * 2 * Math.PI / (anim == "sleep" ? 3.6 : 2.6));

        var sy = Math.Clamp(1 + st.SqY + gy, 0.76, 1.26);
        var sf = Math.Clamp(1 + st.SqF + gf, 0.8, 1.24);
        pose.SqY = sy;
        pose.SqF = sf;
        pose.SqZ = Math.Clamp(1 / Math.Sqrt(sy * sf), 0.85, 1.2);

        // Secondary motion: the head bobbles after the body and the tail whips behind it.
        if (free)
        {
            var acc = Math.Clamp(m.Acceleration, -500, 500);
            Spring(ref st.HeadX, ref st.HeadXV, -acc * 0.005, 3.6, 0.35, dt);
            Spring(ref st.HeadY, ref st.HeadYV, -(sy - 1) * d.HeadR * 0.7, 3.6, 0.32, dt);
            Spring(ref st.TailX, ref st.TailXV, -acc * 0.009, 2.2, 0.25, dt);
            Spring(ref st.TailY, ref st.TailYV, (1 - sy) * d.BodyR * 2.2, 2.2, 0.25, dt);
            pose.Head += new V3(st.HeadX, st.HeadY, 0);
            var n = pose.Tail.Count;
            for (var i = 1; i < n; i++)
            {
                var f = Math.Pow(i / (double)(n - 1), 1.5);
                pose.Tail[i] += new V3(st.TailX * f, st.TailY * f, 0);
            }
        }
        else
        {
            st.HeadX = st.HeadY = st.TailX = st.TailY = 0;
            st.HeadXV = st.HeadYV = st.TailXV = st.TailYV = 0;
        }
        st.HeadScreen = cam.Project(Squashed(pose, pose.Head));

        // Grew up or an anniversary: a burst of confetti and a happy hop.
        if (pet.Celebration != Growth.Event.None)
        {
            pet.Celebration = Growth.Event.None;
            st.SqYV += 1.8;
            SetEmote(st, Emote.Note);
            var head = new Vec2(pet.Position.X + (float)(st.HeadScreen.X * depth), pet.Position.Y + (float)(st.HeadScreen.Y * depth));
            for (var i = 0; i < 28; i++)
            {
                var a = -Math.PI / 2 + (st.Rng.NextDouble() - 0.5) * 2.2;
                var sp = 160 + st.Rng.NextDouble() * 180;
                st.Fx.Add(new Fx
                {
                    Kind = FxKind.Confetti, X = head.X, Y = head.Y - 10,
                    Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp,
                    Life = 1.4 + st.Rng.NextDouble() * 0.6, Size = 5 + st.Rng.NextDouble() * 3, Seed = st.Rng.Next(),
                });
            }
        }

        // Effects age.
        st.EmoteAge += dt;
        for (var i = st.Fx.Count - 1; i >= 0; i--)
        {
            var fx = st.Fx[i];
            fx.Age += dt;
            if (fx.Age >= fx.Life) { st.Fx.RemoveAt(i); continue; }
            var drag = Math.Exp(-dt * (fx.Kind == FxKind.Confetti ? 1.5 : 4));
            fx.Vx *= drag;
            fx.Vy = fx.Kind == FxKind.Confetti ? fx.Vy * drag + 260 * dt : fx.Vy * drag - 10 * dt;
            fx.X += fx.Vx * dt;
            fx.Y += fx.Vy * dt;
        }
    }

    /// <summary>A little striped party hat on the head, worn on anniversary days.</summary>
    private static void PartyHat(DrawingContext ctx, Dims d, Point o, AnimState st)
    {
        var top = new Point(o.X + st.HeadScreen.X + d.HeadR * 0.15, o.Y + st.HeadScreen.Y - d.HeadR * 0.75);
        var h = d.HeadR * 1.15;
        var w = d.HeadR * 0.55;
        var ink = new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x3A, 0x22)), 1.3, lineJoin: PenLineJoin.Round);
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            gc.BeginFigure(new Point(top.X - w, top.Y), true);
            gc.LineTo(new Point(top.X + w, top.Y));
            gc.LineTo(new Point(top.X + w * 0.25, top.Y - h));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x6B, 0xC5, 0xF2)), ink, g);
        var stripe = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x5C)), 2.2);
        for (var i = 1; i <= 2; i++)
        {
            var t = i / 3.0;
            var y = top.Y - h * t;
            var half = w * (1 - t);
            var cx = top.X + w * 0.25 * t;
            ctx.DrawLine(stripe, new Point(cx - half + 1, y), new Point(cx + half - 1, y));
        }
        ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xF2, 0x6B, 0x8E)), ink, new Point(top.X + w * 0.25, top.Y - h), 3.2, 3.2);
    }

    private static V3 Squashed(Pose p, V3 v) => new(v.X * p.SqF, v.Y * p.SqY, v.Z * p.SqZ);

    private static void SetEmote(AnimState st, Emote e)
    {
        st.Emote = e;
        st.EmoteAge = 0;
    }

    /// <summary>Cartoon dust clouds at the feet, <paramref name="along"/> model units ahead (+) or behind (-).</summary>
    private static void Puff(Pet pet, Camera cam, AnimState st, double along, int count, double size, bool spread)
    {
        if (st.Fx.Count > 24) return;
        var rng = st.Rng!;
        for (var i = 0; i < count; i++)
        {
            var side = spread ? (i % 2 == 0 ? 1 : -1) * (12 + rng.NextDouble() * 10) : (rng.NextDouble() - 0.5) * 14;
            var at = cam.Project(new V3(along, 0, side));
            var dir = cam.Project(new V3(spread ? 0 : -1, 0, spread ? Math.Sign(side) : 0));
            var len = Math.Max(0.3, Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y));
            var sp = 22 + rng.NextDouble() * 18;
            st.Fx.Add(new Fx
            {
                Kind = FxKind.Dust,
                X = pet.Position.X + at.X * st.Depth,
                Y = pet.Position.Y + at.Y * st.Depth,
                Vx = dir.X / len * sp,
                Vy = dir.Y / len * sp * 0.5 - 8,
                Life = 0.42 + rng.NextDouble() * 0.18,
                Size = (7 + rng.NextDouble() * 3) * size,
                Seed = rng.Next(),
            });
        }
    }

    private static void Star(Pet pet, Camera cam, AnimState st, double along, double size)
    {
        var at = cam.Project(new V3(along, 6, 0));
        st.Fx.Add(new Fx
        {
            Kind = FxKind.Star,
            X = pet.Position.X + at.X * st.Depth,
            Y = pet.Position.Y + at.Y * st.Depth,
            Life = 0.35,
            Size = 14 * size,
            Seed = st.Rng!.Next(),
        });
    }

    private static readonly Color DustFill = Color.FromRgb(0xF6, 0xEF, 0xE2);
    private static readonly Color BangFill = Color.FromRgb(0xFF, 0xD8, 0x3A);
    private static readonly Color AngerRed = Color.FromRgb(0xE8, 0x3A, 0x34);
    private static readonly Color NoteFill = Color.FromRgb(0x5A, 0xA8, 0xF2);
    private static readonly Color SweatFill = Color.FromRgb(0x9C, 0xD8, 0xFF);

    /// <summary>Dust, impact stars, speed lines and emote marks, drawn flat like a 2D cartoon.</summary>
    private static void DrawCartoonFx(DrawingContext ctx, Pet pet, Dims d, Point o)
    {
        if (!Toon || !States.TryGetValue(pet, out var st)) return;
        var (lr, lg, lb) = LineColor(pet);
        var ink = Color.FromRgb((byte)(lr * 255), (byte)(lg * 255), (byte)(lb * 255));
        var depth = Math.Max(0.2, st.Depth);

        foreach (var fx in st.Fx)
        {
            var k = fx.Age / fx.Life;
            var c = new Point(o.X + (fx.X - pet.Position.X) / depth, o.Y + (fx.Y - pet.Position.Y) / depth);
            if (fx.Kind == FxKind.Confetti)
            {
                Color[] cc = { Color.FromRgb(0xF2, 0x6B, 0x8E), Color.FromRgb(0xFF, 0xD1, 0x5C), Color.FromRgb(0x6B, 0xC5, 0xF2), Color.FromRgb(0x8E, 0xD8, 0x7A), Color.FromRgb(0xB0, 0x8A, 0xF0) };
                var col = cc[fx.Seed % cc.Length];
                var a = (byte)(255 * Math.Clamp((1 - k) * 3, 0, 1));
                var spin = fx.Age * (6 + fx.Seed % 5) + fx.Seed;
                var w = fx.Size * Math.Abs(Math.Cos(spin));
                using (ctx.PushTransform(Matrix.CreateRotation(spin * 0.5) * Matrix.CreateTranslation(c.X, c.Y)))
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, col.R, col.G, col.B)), null, new Rect(-w / 2, -fx.Size * 0.4, Math.Max(0.6, w), fx.Size * 0.8));
                continue;
            }
            if (fx.Kind == FxKind.Dust)
            {
                var a = (byte)(235 * (1 - k * k));
                var r = fx.Size * (0.55 + 0.75 * Math.Sqrt(k));
                var fill = new SolidColorBrush(Color.FromArgb(a, DustFill.R, DustFill.G, DustFill.B));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(a * 0.8), ink.R, ink.G, ink.B)), 1.1);
                var j = fx.Seed % 3;
                ctx.DrawEllipse(fill, pen, new Point(c.X - r * 0.55, c.Y + r * 0.1), r * 0.7, r * 0.6);
                ctx.DrawEllipse(fill, pen, new Point(c.X + r * 0.55, c.Y + r * 0.15), r * 0.62, r * 0.55);
                ctx.DrawEllipse(fill, pen, new Point(c.X + (j - 1) * r * 0.1, c.Y - r * 0.25), r * 0.8, r * 0.7);
            }
            else
            {
                var s = fx.Size * EaseOutBack(Math.Min(1, k * 3), 2) * (1 - 0.3 * k);
                var a = (byte)(255 * (1 - k * k));
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var ang = i * Math.PI / 4 + fx.Seed % 7 * 0.1;
                        var rr = i % 2 == 0 ? s : s * 0.42;
                        var pt = new Point(c.X + Math.Cos(ang) * rr, c.Y + Math.Sin(ang) * rr);
                        if (i == 0) gc.BeginFigure(pt, true); else gc.LineTo(pt);
                    }
                    gc.EndFigure(true);
                }
                ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(a, 0xFF, 0xF2, 0x9A)),
                    new Pen(new SolidColorBrush(Color.FromArgb(a, ink.R, ink.G, ink.B)), 1.3), g);
            }
        }

        // Speed lines streaming off the back while galloping; redrawn on twos like hand-drawn frames.
        var speedN = pet.Motion.GroundSpeed / 140.0;
        if (pet.Animation == "run" && speedN > 0.55)
        {
            var cam = new Camera(pet.Heading);
            var back = cam.Project(new V3(-1, 0, 0));
            var bl = Math.Sqrt(back.X * back.X + back.Y * back.Y);
            if (bl > 0.35)
            {
                var bx = back.X / bl;
                var by = back.Y / bl;
                var frame = (int)(st.Time * 12);
                var rng = new Random(frame * 7919 + pet.Id.GetHashCode());
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(150 * Math.Min(1, speedN)), 0xFF, 0xFF, 0xFF)), 1.8, lineCap: PenLineCap.Round);
                var shade = new Pen(new SolidColorBrush(Color.FromArgb((byte)(110 * Math.Min(1, speedN)), ink.R, ink.G, ink.B)), 1.0, lineCap: PenLineCap.Round);
                for (var i = 0; i < 3; i++)
                {
                    var h = d.ShoulderH * (0.55 + 0.4 * i) * CosP;
                    var start = d.BodyLen * (0.7 + 0.15 * rng.NextDouble());
                    var len = 12 + rng.NextDouble() * 14;
                    var p1 = new Point(o.X + bx * start, o.Y + by * start * 0.4 - h);
                    var p2 = new Point(p1.X + bx * len, p1.Y + by * len * 0.4);
                    ctx.DrawLine(shade, new Point(p1.X, p1.Y + 1.2), new Point(p2.X, p2.Y + 1.2));
                    ctx.DrawLine(pen, p1, p2);
                }
            }
        }

        if (pet.Growth.IsAnniversaryToday && !pet.Hidden && pet.Animation != "sleep") PartyHat(ctx, d, o, st);

        // Emote marks pop in with an overshoot, hold, and fade.
        var life = st.Emote == Emote.Bang ? 1.0 : 1.4;
        if (st.Emote == Emote.None || st.EmoteAge > life) return;
        var t = st.EmoteAge;
        var pop = 1.35 * Math.Max(0.01, EaseOutBack(Math.Min(1, t / 0.2), 2.2));
        var fade = Math.Clamp((life - t) / 0.25, 0, 1);
        var alpha = (byte)(255 * fade);
        var head = new Point(o.X + st.HeadScreen.X, o.Y + st.HeadScreen.Y);
        var side = st.HeadScreen.X >= 0 ? 1 : -1;
        var anchor = new Point(head.X + side * d.HeadR * 0.9, head.Y - d.HeadR * 1.45);
        var inkPen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B)), 1.5, lineJoin: PenLineJoin.Round);
        using var _ = ctx.PushTransform(Matrix.CreateTranslation(-anchor.X, -anchor.Y) * Matrix.CreateScale(pop, pop) * Matrix.CreateTranslation(anchor.X, anchor.Y));

        switch (st.Emote)
        {
            case Emote.Bang:
            {
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    gc.BeginFigure(new Point(anchor.X - 3.6, anchor.Y - 14), true);
                    gc.LineTo(new Point(anchor.X + 3.6, anchor.Y - 14));
                    gc.LineTo(new Point(anchor.X + 1.6, anchor.Y - 1));
                    gc.LineTo(new Point(anchor.X - 1.6, anchor.Y - 1));
                    gc.EndFigure(true);
                }
                var fill = new SolidColorBrush(Color.FromArgb(alpha, BangFill.R, BangFill.G, BangFill.B));
                ctx.DrawGeometry(fill, inkPen, g);
                ctx.DrawEllipse(fill, inkPen, new Point(anchor.X, anchor.Y + 3.5), 2.4, 2.4);
                // Surprise strokes around the head.
                var strokes = new Pen(new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B)), 2.0, lineCap: PenLineCap.Round);
                for (var i = -1; i <= 1; i++)
                {
                    var ang = -Math.PI / 2 + i * 0.55 - side * 0.35;
                    var r1 = d.HeadR * 1.15;
                    var r2 = d.HeadR * 1.15 + 9;
                    ctx.DrawLine(strokes, new Point(head.X + Math.Cos(ang) * r1, head.Y + Math.Sin(ang) * r1),
                        new Point(head.X + Math.Cos(ang) * r2, head.Y + Math.Sin(ang) * r2));
                }
                break;
            }
            case Emote.Anger:
            {
                var throb = 1 + 0.12 * Math.Sin(t * 18);
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, AngerRed.R, AngerRed.G, AngerRed.B)), 2.4, lineCap: PenLineCap.Round);
                var r = 5.5 * throb;
                for (var q = 0; q < 4; q++)
                {
                    var sx = q % 2 == 0 ? 1 : -1;
                    var syy = q < 2 ? 1 : -1;
                    var g = new StreamGeometry();
                    using (var gc = g.Open())
                    {
                        gc.BeginFigure(new Point(anchor.X + sx * r * 0.35, anchor.Y + syy * r), false);
                        gc.QuadraticBezierTo(new Point(anchor.X + sx * r * 0.35, anchor.Y + syy * r * 0.35),
                            new Point(anchor.X + sx * r, anchor.Y + syy * r * 0.35));
                        gc.EndFigure(false);
                    }
                    ctx.DrawGeometry(null, pen, g);
                }
                break;
            }
            case Emote.Note:
            {
                var rise = t * 10;
                var sway = Math.Sin(t * 6) * 2.5;
                var c = new Point(anchor.X + sway, anchor.Y - rise);
                var fill = new SolidColorBrush(Color.FromArgb(alpha, NoteFill.R, NoteFill.G, NoteFill.B));
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    gc.BeginFigure(new Point(c.X + 2.6, c.Y), true);
                    gc.LineTo(new Point(c.X + 2.6, c.Y - 13));
                    gc.QuadraticBezierTo(new Point(c.X + 4, c.Y - 9), new Point(c.X + 8.5, c.Y - 7.5));
                    gc.LineTo(new Point(c.X + 8.5, c.Y - 5.5));
                    gc.QuadraticBezierTo(new Point(c.X + 5, c.Y - 7), new Point(c.X + 4.2, c.Y - 9));
                    gc.LineTo(new Point(c.X + 4.2, c.Y));
                    gc.EndFigure(true);
                }
                ctx.DrawGeometry(fill, inkPen, g);
                ctx.DrawEllipse(fill, inkPen, new Point(c.X, c.Y + 0.5), 3.6, 2.8);
                break;
            }
            case Emote.Sweat:
            {
                var slide = Math.Min(t, 0.8) * 5;
                var c = new Point(head.X + side * d.HeadR * 1.0, head.Y - d.HeadR * 0.6 + slide);
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    gc.BeginFigure(new Point(c.X, c.Y - 8), true);
                    gc.QuadraticBezierTo(new Point(c.X + 5, c.Y - 1), new Point(c.X, c.Y + 2.5));
                    gc.QuadraticBezierTo(new Point(c.X - 5, c.Y - 1), new Point(c.X, c.Y - 8));
                    gc.EndFigure(true);
                }
                ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(alpha, SweatFill.R, SweatFill.G, SweatFill.B)), inkPen, g);
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)), null, new Point(c.X - 1.2, c.Y - 1), 1.1, 1.6);
                break;
            }
        }
    }

    /// <summary>Crossfade length between two clips: slow for settling down, snappy for reflexes.</summary>
    private static double BlendTime(string from, string to)
    {
        if (to is "pounce" or "startle" or "bat" or "hiss" or "shake") return 0.1;
        if (from is "sit" or "lie" or "sleep" or "eat" or "groom" or "stretch" || to is "sit" or "lie" or "sleep" or "eat" or "groom" or "stretch")
            return 0.42;
        if (from is "walk" or "run" && to is "walk" or "run") return 0.3;
        return 0.22;
    }

    /// <summary>Hip or shoulder socket for leg i (0 RF, 1 LF, 2 RH, 3 LH); hind sockets move to the haunch when folded.</summary>
    private static V3 LegJoint(Pose p, Dims d, int i)
    {
        var front = i < 2;
        var z = d.BodyR * (front ? 0.55 : 0.5) * (i % 2 == 0 ? 1 : -1);
        var b = front ? p.Shoulder : p.Hip;
        var joint = new V3(b.X, b.Y - d.BodyR * 0.25, z);
        if (front || p.Fold <= 0) return joint;
        return V3.Lerp(joint, new V3(p.Hip.X, p.Hip.Y, Math.Sign(z) * d.BodyR * 0.75), p.Fold);
    }

    private static Pose BlendPoses(Pose a, Pose b, double w, Dims d, double? bodyW = null)
    {
        var r = b.Clone();
        var wb = bodyW ?? w;
        r.Shoulder = V3.Lerp(a.Shoulder, b.Shoulder, wb);
        r.Hip = V3.Lerp(a.Hip, b.Hip, wb);
        r.Head = V3.Lerp(a.Head, b.Head, wb);
        r.HeadYaw = a.HeadYaw + (b.HeadYaw - a.HeadYaw) * w;
        r.HeadPitch = a.HeadPitch + (b.HeadPitch - a.HeadPitch) * w;
        r.Fold = a.Fold + (b.Fold - a.Fold) * w;
        r.HindFolded = r.Fold > 0.5;
        for (var i = 0; i < 4; i++)
        {
            if (a.Feet[i] is { } fa && b.Feet[i] is { } fb)
            {
                // Knees: explicit where a pose gives them, solved where it does not, then blended.
                var ka = a.Knees[i] ?? SolveKnee(LegJoint(a, d, i), fa, d.L1, d.L2);
                var kb = b.Knees[i] ?? SolveKnee(LegJoint(b, d, i), fb, d.L1, d.L2);
                r.Feet[i] = V3.Lerp(fa, fb, w);
                r.Knees[i] = a.Knees[i] is null && b.Knees[i] is null ? null : V3.Lerp(ka, kb, w);
            }
            else
            {
                r.Feet[i] = w < 0.5 ? a.Feet[i] : b.Feet[i];
                r.Knees[i] = w < 0.5 ? a.Knees[i] : b.Knees[i];
            }
        }
        r.TailWag = a.TailWag + (b.TailWag - a.TailWag) * w;
        r.TailLift = a.TailLift + (b.TailLift - a.TailLift) * w;
        r.Mouth = a.Mouth + (b.Mouth - a.Mouth) * w;
        r.EarBack = a.EarBack + (b.EarBack - a.EarBack) * w;
        if (w < 0.35) r.Eyes = a.Eyes;
        if (w < 0.5)
        {
            r.Tongue = a.Tongue;
            r.TailOnGround = a.TailOnGround;
        }
        if (a.Tail.Count >= 2 && b.Tail.Count >= 2)
        {
            var n = Math.Max(a.Tail.Count, b.Tail.Count);
            var ta = Resample(a.Tail, n);
            var tb = Resample(b.Tail, n);
            r.Tail = new List<V3>(n);
            for (var i = 0; i < n; i++) r.Tail.Add(V3.Lerp(ta[i], tb[i], wb));
        }
        return r;
    }

    /// <summary>A polyline resampled to n points evenly along its index range.</summary>
    private static List<V3> Resample(List<V3> pts, int n)
    {
        if (pts.Count == n) return pts;
        var r = new List<V3>(n);
        for (var i = 0; i < n; i++)
        {
            var f = i * (pts.Count - 1) / (double)(n - 1);
            var j = Math.Min((int)f, pts.Count - 2);
            r.Add(V3.Lerp(pts[j], pts[j + 1], f - j));
        }
        return r;
    }

    /// <summary>
    /// Drawn-cartoon cheat: a profile shows the face three-quarter on, the way an animator would draw it,
    /// instead of a flat side view with one eye on the silhouette edge. Seen from behind nothing changes.
    /// </summary>
    private static void FaceViewer(Pose pose, Camera cam, string anim)
    {
        if (anim is not ("idle" or "walk" or "sit" or "lie" or "petted" or "run" or "stretch" or "pant")) return;
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
        var breedLook = BreedLook.Of(pet);
        var lowerLeg = breedLook.PointLegs && breedLook.Point is { } legPoint ? Mix(style.Leg, BreedLook.C(legPoint), 0.75) : style.Leg;

        // Legs: upper (joint → knee) and lower (knee → foot) tubes, paw spheres.
        s.K = 1.2;
        for (var i = 0; i < 4; i++)
        {
            if (p.Feet[i] is not { } foot) continue;
            var joint = LegJoint(p, d, i);
            var knee = p.Knees[i] ?? SolveKnee(joint, foot, d.L1, d.L2);
            var upperR = d.BodyR * (i < 2 ? 0.3 : 0.34);
            var lowerR = d.BodyR * 0.24;
            s.Capsule(joint, knee, upperR, style.Leg, k: 2.5);
            s.Capsule(knee, foot + new V3(0, d.BodyR * 0.2, 0), lowerR, lowerLeg, k: 1.2);
            s.Sphere(foot + new V3(1.5, d.BodyR * 0.28, 0), d.BodyR * 0.42, pawColor, k: 1.2);
        }

        // Folded haunches.
        s.K = 3;
        if (p.Fold > 0.02)
        {
            var hr = d.BodyR * (0.45 + 0.5 * p.Fold);
            s.Sphere(new V3(p.Hip.X + 1, p.Hip.Y, d.BodyR * 0.55 * p.Fold), hr, fur);
            s.Sphere(new V3(p.Hip.X + 1, p.Hip.Y, -d.BodyR * 0.55 * p.Fold), hr, fur);
        }
        s.K = 4;

        // Trunk + chest + rump.
        var cat = pet.Species == PetSpecies.Cat;
        var look = BreedLook.Of(pet);
        var striped = look.Stripes;
        s.Stripe = striped ? 1 : 0;
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
        s.Stripe = striped ? 1 : 0;
        s.Capsule(trunkA + new V3(0, d.BodyR * 0.55, 0), trunkB + new V3(-d.BodyR * 0.4, d.BodyR * 0.5, 0), d.BodyR * 0.62, style.Saddle, k: 2.5);
        s.Stripe = 0;
        s.Sphere(trunkB + new V3(d.BodyR * 0.55, -d.BodyR * 0.2, 0), d.BodyR * (pom ? 0.95 : 0.95), style.Chest, k: 4);
        s.Sphere(V3.Lerp(trunkB, p.Head, 0.4) + new V3(d.BodyR * 0.2, -d.BodyR * 0.25, 0), d.BodyR * (pom ? 0.85 : 0.8), style.Chest, k: 4);
        if (mud >= 0.7f)
            s.Capsule(trunkA + new V3(6, d.BodyR * 0.6, 0), trunkB + new V3(-6, d.BodyR * 0.6, 0), d.BodyR * 0.35, MudColor(pet), k: 1);
        BuildCoat(s, d, style, p, look, trunkA, trunkB);

        // Tail.
        s.Stripe = striped ? 1 : 0;
        s.K = 2.5;
        BuildTail(s, pet, d, style, p, t);

        // Neck.
        s.Stripe = striped ? 0.5 : 0;
        s.K = 4;
        var neckFrom = trunkB + new V3(0, d.BodyR * 0.1, 0);
        var neckTo = p.Head + new V3(-d.HeadR * 0.3, -d.HeadR * 0.2, 0);
        s.Capsule(neckFrom, neckTo, d.BodyR * 0.58, fur);
        if (cat && Toon) BuildCollar(s, d, V3.Lerp(neckFrom, neckTo, 0.42), (neckTo - neckFrom).Normalized);

        BuildHead(s, pet, d, style, p, t);
    }

    /// <summary>
    /// The breed's coat beyond its colour (see <see cref="CoatKind"/>): ruffs, curls, long silky hair, britches,
    /// feathering, and a Bengal's rosettes.
    /// </summary>
    private static void BuildCoat(Scene s, Dims d, Style style, Pose p, BreedLook look, V3 trunkA, V3 trunkB)
    {
        var r = d.BodyR;
        var keepK = s.K;
        var keepStripe = s.Stripe;
        s.Stripe = 0;
        var neck = V3.Lerp(trunkB, p.Head, 0.3);
        switch (look.Coat)
        {
            case CoatKind.Fluffy:
                // A big ruff of fur round the neck and chest (Pomeranian, Husky, Shiba, Jindo) and a fluffy rump.
                s.K = 4;
                s.Sphere(neck + new V3(r * 0.15, -r * 0.1, 0), r * 1.12, Mix(style.Fur, style.Chest, 0.5));
                s.Sphere(trunkA + new V3(-r * 0.2, r * 0.1, 0), r * 1.1, style.Fur);
                break;
            case CoatKind.Long:
                // A long-haired cat: a mane round the neck, "britches" on the hind legs, a fringe under the belly.
                s.K = 4;
                s.Sphere(neck + new V3(r * 0.1, -r * 0.15, 0), r * 1.2, style.Chest);
                foreach (var z in new[] { -1, 1 })
                    s.Sphere(new V3(p.Hip.X - r * 0.1, Math.Max(r * 0.7, p.Hip.Y - r * 0.35), z * r * 0.55), r * 0.75, style.Fur);
                s.Capsule(trunkA + new V3(4, -r * 0.7, 0), trunkB + new V3(-4, -r * 0.7, 0), r * 0.5, Mix(style.Fur, style.Belly, 0.5), k: 3);
                break;
            case CoatKind.Feathered:
                // A retriever: a full pale chest and feathering hanging under the belly.
                s.K = 3;
                s.Sphere(neck + new V3(r * 0.3, -r * 0.35, 0), r * 0.9, Mix(style.Fur, style.Chest, 0.5));
                s.Capsule(trunkA + new V3(r * 0.6, -r * 0.75, 0), trunkB + new V3(-r * 0.2, -r * 0.85, 0), r * 0.42, Mix(style.Fur, style.Chest, 0.4), k: 2.5);
                break;
            case CoatKind.Silky:
                // A Maltese: long silky hair parted along the back, falling almost to the floor over the legs.
                if (p.Fold < 0.5)
                {
                    s.K = 3;
                    foreach (var z in new[] { -1, 1 })
                        s.Capsule(new V3(trunkA.X - r * 0.2, r * 0.65, z * r * 0.5), new V3(trunkB.X + r * 0.1, r * 0.65, z * r * 0.5), r * 0.62, style.Fur);
                }
                break;
            case CoatKind.Curly:
                // Tight curls (Poodle, Bichon): a bumpy outline of little puffs over the back and sides, puffs on the legs.
                s.K = 1.2;
                for (var i = 0; i < 4; i++)
                {
                    var c = V3.Lerp(trunkA, trunkB, (i + 0.5) / 4);
                    s.Sphere(c + new V3(0, r * 0.85, 0), r * 0.42, style.Fur);
                    s.Sphere(c + new V3(0, r * 0.25, r * 0.85), r * 0.38, style.Fur);
                    s.Sphere(c + new V3(0, r * 0.25, -r * 0.85), r * 0.38, style.Fur);
                }
                for (var i = 0; i < 4; i++)
                    if (p.Feet[i] is { } f)
                        s.Sphere(f + new V3(0, d.L2 * 0.55, 0), r * 0.4, style.Fur);
                break;
        }
        if (look.Spots)
        {
            // Bengal rosettes: dark spots scattered along both flanks (in place of tabby stripes).
            var spot = Mix(style.Saddle, style.Dark, 0.35);
            for (var i = 0; i < 5; i++)
            {
                // From the haunch to the shoulder; the round rump and chest stick out further than the middle.
                var f = -0.15 + 0.3 * i;
                var c = V3.Lerp(trunkA, trunkB, f);
                var bulge = i is 0 or 4 ? 1.12 : i is 1 or 3 ? 1.0 : 0.94;
                foreach (var z in new[] { -1, 1 })
                {
                    var high = (i + (z > 0 ? 1 : 0)) % 2 == 0;
                    s.Sphere(c + new V3(z * (i % 2 == 0 ? 1.5 : -1.5), r * (high ? 0.55 : 0.15), z * r * bulge * (high ? 0.84 : 0.97)),
                        r * 0.26, spot, edge: false, k: 0, gloss: 0);
                }
            }
        }
        s.K = keepK;
        s.Stripe = keepStripe;
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

    private static void BuildTail(Scene s, Pet pet, Dims d, Style style, Pose p, float t)
    {
        var species = pet.Species;
        var look = BreedLook.Of(pet);
        var pts = p.Tail.Count >= 2 ? p.Tail : TailPoints(species, d, p, t);
        var point = look.Point is { } pc ? BreedLook.C(pc) : (Color?)null;
        if (species == PetSpecies.Dog)
        {
            switch (look.Tail)
            {
                case TailKind.DogThin:
                case TailKind.DogFeather:
                {
                    // A long tail carried out behind: a thin whip (Dachshund, Chihuahua, Beagle) or a heavy feathered
                    // one (Golden Retriever). Hangs when carried, wags side to side.
                    var feather = look.Tail == TailKind.DogFeather;
                    var line = p.Tail.Count >= 2 ? p.Tail : LongDogTail(d, p, t, feather);
                    for (var i = 0; i < line.Count - 1; i++)
                    {
                        var f = (double)i / (line.Count - 1);
                        var radius = d.BodyR * (feather ? 0.42 - 0.18 * f : 0.2 - 0.08 * f);
                        var c = i == line.Count - 2 && look.TailTip is { } tip ? BreedLook.C(tip)
                            : feather && i > 0 ? Mix(style.Fur, style.Chest, 0.35) : style.Fur;
                        s.Capsule(line[i], line[i + 1], radius, c);
                    }
                    return;
                }
                case TailKind.DogStub:
                    // A Corgi's little bob: just a tuft on the rump.
                    s.Sphere(pts[0] + new V3(-d.BodyR * 0.1, d.BodyR * 0.05, 0), d.BodyR * 0.36, style.Fur);
                    return;
                case TailKind.DogPompom:
                {
                    // A Poodle's thin tail with a round pompom at the end.
                    for (var i = 0; i < pts.Count - 1; i++)
                        s.Capsule(pts[i], pts[i + 1], d.BodyR * 0.2, style.Fur);
                    s.Sphere(pts[^1], d.BodyR * 0.58, Lighten(style.Fur, 0.08));
                    return;
                }
            }
            var plume = look.Tail == TailKind.DogPlume ? 0.6 : 0.5;
            var colors = new[] { style.Fur, style.Fur, Mix(style.Fur, style.Chest, 0.6) };
            for (var i = 0; i < pts.Count - 1; i++)
            {
                var ra = d.BodyR * (plume - 0.06 * i);
                var rb = d.BodyR * (plume - 0.06 * (i + 1));
                s.Capsule(pts[i], pts[i + 1], (ra + rb) / 2, colors[Math.Min(i, colors.Length - 1)]);
            }
            return;
        }

        var fluffy = look.Tail == TailKind.CatPlume;
        var thick = fluffy ? 0.6 : Toon ? 0.44 : 0.36;
        var taper = fluffy ? 0.025 : Toon ? 0.04 : 0.05;
        for (var i = 0; i < pts.Count - 1; i++)
        {
            var c = point ?? (look.Stripes || look.Spots ? (i % 2 == 0 ? style.Fur : style.Saddle) : style.Fur);
            s.Capsule(pts[i], pts[i + 1], d.BodyR * (thick - taper * i), c);
        }
        if (Toon)
            s.Sphere(pts[^1], d.BodyR * (thick - taper * (pts.Count - 1)) * 1.05, point ?? (look.Stripes || look.Spots ? style.Saddle : style.Fur));
    }

    /// <summary>A long dog tail carried out behind and a little up (down and hanging when carried).</summary>
    private static List<V3> LongDogTail(Dims d, Pose p, float t, bool feather)
    {
        var root = new V3(p.Hip.X - d.BodyR * 0.55, p.Hip.Y + d.BodyR * 0.45, 0);
        var sway = Math.Sin(t * 2.5 * p.TailWag) * (0.3 + 0.15 * Math.Min(3, p.TailWag));
        var seg = d.BodyLen * (feather ? 0.24 : 0.2);
        var rise = p.TailLift < -0.5 ? -0.9 : feather ? 0.25 : 0.55;
        var pts = new List<V3> { root };
        for (var i = 1; i <= 3; i++)
            pts.Add(root + new V3(-seg * i, seg * rise * i * (feather ? 0.6 : 0.8), seg * 0.35 * sway * i));
        return pts;
    }

    /// <summary>Tail centre line (root first) for the pose: a dog's curled plume or a kitten's upright tail.</summary>
    private static List<V3> TailPoints(PetSpecies species, Dims d, Pose p, float t)
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
            return pts;
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
        return pts;
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
        var look = BreedLook.Of(pet);
        var point = look.Point is { } pc ? BreedLook.C(pc) : (Color?)null;
        s.Stripe = persian && look.Stripes && !Toon ? 0.8 : 0;
        s.Sphere(h, r, style.Fur, k: 3);
        s.Stripe = 0;

        // Fluff: a Poodle's topknot, a Bichon's round cloud of a head, a Persian's ruff.
        if (look.HeadFluff > 0)
        {
            var f = look.HeadFluff;
            s.Sphere(At(-0.12, 0.62, 0), r * (0.55 + 0.35 * f), Lighten(style.Fur, 0.04), k: 4);
            s.Sphere(At(-0.2, 0.05, 0.62), r * (0.4 + 0.3 * f), style.Fur, edge: false, k: 4);
            s.Sphere(At(-0.2, 0.05, -0.62), r * (0.4 + 0.3 * f), style.Fur, edge: false, k: 4);
        }

        // Colourpoint (Ragdoll): a darker mask around the eyes and nose.
        if (point is { } mask)
            s.Sphere(At(0.5, 0.14, 0), r * 0.52, Mix(style.Fur, mask, 0.55), edge: false, k: 2.5);

        // A Husky's dark cap over the crown and down the back of the head, the white face mask below it.
        if (look.Cap)
            s.Sphere(At(-0.35, 0.5, 0), r * 0.6, style.Saddle, edge: false, k: 2.5);

        // Fluffy cheeks in the chest colour; a Shiba's or Jindo's pale "urajiro" cheeks.
        var cheek = persian || look.Face == FaceKind.Fox ? style.Chest : Mix(style.Fur, style.Chest, 0.35);
        // Chubby breeds (British Shorthair) get big round jowls.
        // A cat's cheeks sit lower and further out, under the big eyes rather than over their corners.
        var jowl = look.Cheeks;
        var (cf, cu, cs) = persian ? (0.18, -0.4 - 0.14 * (jowl - 1), 0.6 + 0.22 * (jowl - 1)) : (0.3, -0.3 - 0.08 * (jowl - 1), 0.55 + 0.12 * (jowl - 1));
        s.Sphere(At(cf, cu, cs), r * 0.5 * jowl, cheek, edge: false, k: 2.5);
        s.Sphere(At(cf, cu, -cs), r * 0.5 * jowl, cheek, edge: false, k: 2.5);

        // A Corgi's white blaze up the forehead.
        if (look.Blaze)
            s.AddSdf(At(0.78, 0.0, 0), At(0.2, 0.92, 0), r * 0.13, r * 0.2, style.Belly, k: 1.5);

        // Muzzle and nose: Persian face is flat with the nose high between the eyes; Pomeranian has a short fox muzzle.
        if (persian)
        {
            // A Persian's flat face: muzzle and nose pulled in toward the eyes.
            var flat = look.Face == FaceKind.Flat ? 0.12 : 0;
            s.Sphere(At(0.74 - flat, -0.3, 0.22), r * 0.3, style.Belly, edge: false, k: 1.5);
            s.Sphere(At(0.74 - flat, -0.3, -0.22), r * 0.3, style.Belly, edge: false, k: 1.5);
            s.Sphere(At(0.6 - flat, -0.52, 0), r * 0.24, style.Belly, edge: false, k: 1.5);
            s.Sphere(At(0.97 - flat, -0.08 + flat * 0.4, 0), r * 0.1, style.Nose, edge: false, k: 0, gloss: 0.9);

            if (Toon)
            {
                // Pointy fur tufts on the cheeks and the crown, like a drawn kitten (not on a hairless Sphynx).
                foreach (var sign in look.Hairless ? Array.Empty<int>() : new[] { -1, 1 })
                {
                    s.AddSdf(At(0.15, -0.25, sign * 0.8), At(-0.05, -0.52, sign * 1.17), r * 0.2, r * 0.03, style.Fur, k: 1.2);
                    s.AddSdf(At(0.3, -0.45, sign * 0.66), At(0.2, -0.72, sign * 1.02), r * 0.18, r * 0.02, style.Fur, k: 1.2);
                    s.AddSdf(At(-0.05, 0.0, sign * 0.88), At(-0.3, -0.16, sign * 1.15), r * 0.15, r * 0.03, style.Fur, k: 1.2);
                }
                if (!look.Hairless) s.AddSdf(At(-0.15, 0.85, 0.0), At(0.05, 1.15, 0.06), r * 0.14, r * 0.02, style.Fur, k: 1.2);

                // Tabby marks on the forehead: a middle stripe and one on each side.
                // They carry on over the crown to the back of the head so every view shows clean tabby lines.
                if (look.Stripes)
                {
                s.AddSdf(At(0.5, 0.86, 0), At(0.8, 0.6, 0), r * 0.07, r * 0.05, style.Saddle, k: 0, gloss: 0);
                s.AddSdf(At(0.0, 1.0, 0), At(0.5, 0.86, 0), r * 0.08, r * 0.07, style.Saddle, k: 0, gloss: 0);
                s.AddSdf(At(-0.56, 0.83, 0), At(0.0, 1.0, 0), r * 0.07, r * 0.08, style.Saddle, k: 0, gloss: 0);
                foreach (var sign in new[] { -1, 1 })
                {
                    s.AddSdf(At(0.42, 0.84, sign * 0.33), At(0.7, 0.62, sign * 0.35), r * 0.06, r * 0.04, style.Saddle, k: 0, gloss: 0);
                    s.AddSdf(At(-0.05, 0.93, sign * 0.36), At(0.42, 0.84, sign * 0.33), r * 0.065, r * 0.06, style.Saddle, k: 0, gloss: 0);
                    s.AddSdf(At(-0.62, 0.62, sign * 0.42), At(-0.05, 0.93, sign * 0.36), r * 0.05, r * 0.065, style.Saddle, k: 0, gloss: 0);
                }
                }

                // Little "w" mouth under the nose and whisker dots on the muzzle.
                var line = style.Dark;
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
        else if (look.Face == FaceKind.Flat)
        {
            // French Bulldog: a short, wide, pushed-in muzzle with a big dark nose.
            s.Sphere(At(0.66, -0.26, 0.2), r * 0.32, style.Chest, edge: false, k: 2);
            s.Sphere(At(0.66, -0.26, -0.2), r * 0.32, style.Chest, edge: false, k: 2);
            s.Sphere(At(0.92, -0.08, 0), r * 0.18, style.Nose, edge: false, k: 0, gloss: 0.9);
        }
        else if (look.Face == FaceKind.Fox)
        {
            // Shiba, Jindo, Corgi: a longer fox-like snout in the pale "urajiro" colour, nose at the tip. Hounds and
            // retrievers have longer, broader ones in the coat colour (a Dachshund's long nose, a Golden's broad muzzle).
            var snoutLen = look.Snout;
            var broad = look.Size > 1.2f ? 1.15 : 1.0;
            var snout = look.PaleSnout ? style.Chest : Mix(style.Fur, style.Chest, 0.3);
            var end = 0.55 + 0.75 * snoutLen;
            s.AddSdf(At(0.55, -0.2, 0), At(end, -0.3 - 0.06 * (snoutLen - 1), 0), r * 0.4 * broad, r * 0.27 * broad, snout, k: 2);
            s.Sphere(At(end + 0.25, -0.22 - 0.06 * (snoutLen - 1), 0), r * 0.16 * broad, style.Nose, edge: false, k: 0, gloss: 0.9);
        }
        else
        {
            // A button face: a short round muzzle (shorter still on a Pomeranian, a little longer on a Poodle).
            var snoutLen = look.Snout;
            s.Sphere(At(0.62 + 0.16 * snoutLen, -0.22, 0), r * 0.42, style.Chest, edge: false, k: 2);
            s.Sphere(At(0.95 + 0.22 * snoutLen, -0.14, 0), r * 0.17, style.Nose, edge: false, k: 0, gloss: 0.9);
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
            var earColor = point ?? style.Fur;
            if (look.Ears == EarKind.CatFolded)
            {
                // Scottish Fold: small ears folded forward and down like a little cap.
                var keep = s.K;
                s.K = 0.6;
                s.Chain(new[] { At(-0.15, 0.72, sign * 0.45), At(0.08 - tilt * 0.3, 0.95, sign * 0.52), At(0.32 - tilt * 0.3, 0.84, sign * 0.5) },
                    r * 0.22, r * 0.15, earColor);
                s.K = keep;
            }
            else if (persian)
            {
                // Small, round-tipped ears set low and wide, half buried in fur. Small-eared breeds (British, Persian)
                // have shorter, rounder ones.
                var small = look.Ears == EarKind.CatSmall;
                var size = look.EarSize * (small ? 0.62 : 1.0);
                var b1 = At(0.12, 0.55, sign * 0.82);
                var b2 = At(-0.42, 0.6, sign * 0.32);
                var tip = At(-0.15 - tilt, 0.55 + 0.87 * size - tilt * 0.4, sign * (0.8 + (small ? 0.1 : 0)));
                s.Tri(b1, tip, b2, earColor, style.EarInner, fwd * (r * 0.2), tipRound: small ? 0.3 : Toon ? 0.12 : 0.3);
                if (look.EarTufts)
                    s.AddSdf(tip, tip + up * (r * 0.32) + side * (sign * r * 0.04), r * 0.07, r * 0.012, style.Saddle, k: 0.5);
            }
            else if (look.Ears == EarKind.DogErect)
            {
                // Shiba, Jindo, Corgi: upright triangle ears, wide at the base with rounded tips, set apart on the head.
                var size = look.EarSize;
                if (size >= 1.4)
                {
                    // Bat ears (French Bulldog, Chihuahua): big and broad, set wide and flaring out to the sides,
                    // with round tips. Not tall: tall ears read as a rabbit's.
                    var bb1 = At(0.12, 0.5, sign * 0.98);
                    var bb2 = At(-0.48, 0.62, sign * 0.28);
                    var btip = At(-0.2 - tilt, 1.2 + 0.12 * size - tilt * 0.4, sign * (0.95 + 0.3 * (size - 1)));
                    s.Tri(bb1, btip, bb2, earColor, style.EarInner, fwd * (r * 0.2), tipRound: 0.62);
                    continue;
                }
                var b1 = At(0.08, 0.6, sign * 0.88);
                var b2 = At(-0.45, 0.66, sign * 0.3);
                var tip = At(-0.18 - tilt, 0.95 + 0.5 * size - tilt * 0.4, sign * (0.62 + 0.18 * size));
                s.Tri(b1, tip, b2, earColor, style.EarInner, fwd * (r * 0.2), tipRound: 0.32);
            }
            else
            {
                // Dog: soft floppy ears. They rise a little from the top of the head, fold over and hang down beside
                // the cheeks with round, slightly wider tips (a cat's stand up in points). Ears back = pulled up and
                // back; a flap (shaking) swings the hanging part.
                var swing = flap * 0.18;
                var size = look.EarSize;
                // Maltese and Poodle: long ears covered in hair that hang lower and fuller.
                var longEar = look.Ears == EarKind.DogLongFloppy;
                var drop = longEar ? 0.45 : 0;
                var earRoot = At(-0.08, 0.74, sign * 0.5);
                var fold = At(-0.06 - tilt * 0.4, 0.98 + tilt * 0.15, sign * (0.86 * size));
                var hang = At(0.0 - tilt * 0.6, 0.6 - drop * 0.5 + tilt * 0.35, sign * (1.1 * size + swing));
                var tip = At(0.06 - tilt * 0.7, 0.16 - drop + tilt * 0.5, sign * (1.06 * size + swing * 1.4));
                // Pale coats get ears in the coat colour (a white Maltese has white ears), others a shade darker.
                var pale = style.Fur.R + style.Fur.G + style.Fur.B > 690;
                var ear = pale ? style.Fur : Mix(style.Fur, style.Dark, 0.22);
                var keep = s.K;
                s.K = 0.5;                                     // a crisp crease where the ear meets the head
                s.Chain(new[] { earRoot, fold, hang, tip }, r * 0.17 * size, r * (longEar ? 0.34 : 0.27) * size, ear);
                s.K = keep;
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
            var ec = persian ? (Toon ? At(0.6, 0.13, sign * 0.43) : At(0.66, 0.12, sign * 0.38)) : At(0.68, 0.16, sign * 0.38);
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
                    var er = r * (persian ? (Toon ? (wide ? 0.41 : 0.38) : (wide ? 0.42 : 0.38)) : (wide ? 0.34 : 0.31))
                             * (1 + 0.12 * (1 - pet.Growth.BodyGrowth));   // baby eyes are bigger for the head
                    var gaze = (fwd * 0.9 + outward * 0.3 + up * 0.05).Normalized;
                    // Clip to the head surface so the eye sits flush in the face like a drawn-on eye, not a bulging ball.
                    s.AddSdf(ec, h, er, r * 1.01, style.Iris, k: 0, eyeDir: gaze);
                    break;
                }
            }
        }

        // Whiskers fanning out from the muzzle (drawn as lines on top of the 3D body): cats only, in the fur style.
        // The cartoon kitten has whisker dots instead, and dogs have none (whiskers made the puppy look like a cat).
        if (!persian || Toon) return;
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

    /// <summary>Coat colours for little pictures of the pet (the face in the house window).</summary>
    public static (Color Fur, Color Belly, Color Dark, Color Nose, Color EarInner) CoatOf(Pet pet)
    {
        var s = StyleOf(pet);
        return (s.Fur, s.Belly, s.Dark, s.Nose, s.EarInner);
    }

    private static Style StyleOf(Pet pet)
    {
        var style = BaseStyle(pet);
        var s = pet.Growth.Seniority;
        if (s <= 0) return style;
        // Old age: the coat fades a little and the muzzle and chest go white.
        Color Grey(Color c, double k) => Mix(c, Color.FromRgb((byte)((c.R + c.G + c.B) / 3), (byte)((c.R + c.G + c.B) / 3), (byte)((c.R + c.G + c.B) / 3)), k);
        return style with
        {
            Fur = Mix(Grey(style.Fur, 0.3 * s), Colors.White, 0.1 * s),
            Saddle = Mix(Grey(style.Saddle, 0.35 * s), Colors.White, 0.12 * s),
            Belly = Mix(style.Belly, Colors.White, 0.5 * s),
            Chest = Mix(style.Chest, Colors.White, 0.5 * s),
            Leg = Mix(Grey(style.Leg, 0.3 * s), Colors.White, 0.1 * s),
        };
    }

    private static Style BaseStyle(Pet pet)
    {
        var w = pet.Appearance.Wet;
        var l = BreedLook.Of(pet);
        Color W(uint rgb) => Darken(BreedLook.C(rgb), 0.35 * w);
        return new Style(
            Fur: W(l.Fur), Belly: W(l.Belly), Dark: BreedLook.C(l.Dark), Nose: BreedLook.C(l.Nose), EarInner: BreedLook.C(l.EarInner),
            Paw: W(l.Paw), Saddle: W(l.Saddle), Chest: W(l.Chest), Leg: W(l.Leg), Iris: BreedLook.C(l.Iris));
    }

    private static Color MudColor(Pet pet) => pet.Appearance.MudDried ? Color.FromRgb(0x8A, 0x6A, 0x4E) : Color.FromRgb(0x5B, 0x3A, 0x1E);

    private static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
