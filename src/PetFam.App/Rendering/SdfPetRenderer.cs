using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;

namespace PetFam.Desktop.Rendering;

/// <summary>
/// Real 3D rendering of the pet on the GPU: the rig's parts become signed-distance round cones that are
/// smoothly fused, then ray-marched per pixel in an SkSL shader with diffuse/sky/rim/specular lighting,
/// ambient occlusion and a soft shadow cast on the ground. Runs inside Avalonia's Skia GPU surface, so it
/// works in the transparent desktop-layer windows. Falls back to the vector renderer when no GPU context exists.
/// </summary>
public static class SdfPetRenderer
{
    public const int MaxPrims = 96;

    private static volatile bool _enabled = Environment.GetEnvironmentVariable("PETFAM_RENDERER") != "vector";
    private static SKRuntimeEffect? _effect;
    private static bool _compileTried;
    private static readonly object Gate = new();

    public static bool Enabled => _enabled && !Suspended;

    /// <summary>Set while rendering to a bitmap (snapshot): software targets use the vector renderer.</summary>
    public static bool Suspended { get; set; }

    private static bool _warnedTooMany;

    /// <summary>Uniform data for one pet, built on the UI thread and consumed on the render thread.</summary>
    public sealed class Frame
    {
        public readonly float[] P = new float[MaxPrims * 4];
        public readonly float[] Q = new float[MaxPrims * 4];
        public readonly float[] M = new float[MaxPrims * 4];
        public readonly float[] X = new float[MaxPrims * 4];
        public int Count;

        /// <summary>Drawn this many times bigger on a sheet (close-ups).</summary>
        public float Zoom = 1f;
        public float FootX, FootY, Width, Height;
        public float Shadow = 1f;
        public float Fur;

        /// <summary>Curly coat bump height (Poodle, Bichon); 0 for smooth coats.</summary>
        public float Curl;
        public float Clip;
        public float Toon;
        public float[] Line = { 0.5f, 0.28f, 0.12f };
        public float[] Shade = { 0.98f, 0.86f, 0.80f };
        public float[] Stripe = { 0.88f, 0.66f, 0.46f };
        public float YawCos = 1f, YawSin;
        public float WhiskerWidth;
        public float[] Whiskers = Array.Empty<float>();
        public float[] Sq = { 1f, 1f, 1f };
    }

    public static Frame Build(Pet pet, float depth, float footX, float footY, float width, float height)
    {
        var build = PetRenderer.BuildSdf(pet, depth);
        var prims = build.Prims;
        var f = new Frame
        {
            FootX = footX, FootY = footY, Width = width, Height = height,
            Shadow = pet.Hold == HoldState.Carried || pet.Animation == "swim" ? 0f : 1f,
            Clip = pet.Animation == "swim" ? 1f : 0f,
            Toon = PetRenderer.Toon ? 1f : 0f,
            Line = new[] { PetRenderer.LineColor(pet).R, PetRenderer.LineColor(pet).G, PetRenderer.LineColor(pet).B },
            Shade = new[] { PetRenderer.ShadeTint(pet).R, PetRenderer.ShadeTint(pet).G, PetRenderer.ShadeTint(pet).B },
            Stripe = new[] { PetRenderer.StripeTint(pet).R, PetRenderer.StripeTint(pet).G, PetRenderer.StripeTint(pet).B },
            Sq = build.Squash, Fur = build.Fur, Curl = build.Curl, WhiskerWidth = build.WhiskerWidth, Whiskers = build.Whiskers.ToArray(),
            YawCos = MathF.Cos(pet.Heading * MathF.PI / 180f), YawSin = MathF.Sin(pet.Heading * MathF.PI / 180f),
        };
        var n = Math.Min(prims.Count, MaxPrims);
        if (prims.Count > MaxPrims && !_warnedTooMany)
        {
            _warnedTooMany = true;
            AppLog.Write($"3D renderer: {pet.Species} has {prims.Count} parts, only {MaxPrims} fit; hard parts (eyes, nose) may be dropped");
        }
        for (var i = 0; i < n; i++)
        {
            var p = prims[i];
            var j = i * 4;
            f.P[j] = p.Ax; f.P[j + 1] = p.Ay; f.P[j + 2] = p.Az; f.P[j + 3] = p.R1;
            f.Q[j] = p.Bx; f.Q[j + 1] = p.By; f.Q[j + 2] = p.Bz; f.Q[j + 3] = p.R2;
            f.M[j] = p.Cr; f.M[j + 1] = p.Cg; f.M[j + 2] = p.Cb; f.M[j + 3] = p.K;
            f.X[j] = p.Gloss; f.X[j + 1] = p.Aux1; f.X[j + 2] = p.Aux2; f.X[j + 3] = p.Aux3;
        }
        f.Count = n;
        return f;
    }

    /// <summary>Only the body (no bubbles or "z z").</summary>
    public static void PaintBody(DrawingContext ctx, Pet pet, float depth, float footX, float footY, float width, float height) =>
        ctx.Custom(new Op(Build(pet, depth, footX, footY, width, height)));

    /// <summary>Draws the pet in sprite-local model units (feet at footX/footY) plus 2D overlays.</summary>
    public static void Paint(DrawingContext ctx, Pet pet, float depth, float footX, float footY, float width, float height)
    {
        ctx.Custom(new Op(Build(pet, depth, footX, footY, width, height)));
        PetRenderer.DrawOverlays(ctx, pet, new Vec2(footX, footY));
    }

    // ------------------------------------------------------------------ debug sheet (rendered on the GPU)

    private static List<(float X, float Y, Frame Frame)>? _pendingSheet;
    private static (int W, int H, string Path)? _sheetTarget;

    public static void QueueSheet(List<(float X, float Y, Frame Frame)> cells, int width, int height, string path)
    {
        lock (Gate)
        {
            _pendingSheet = cells;
            _sheetTarget = (width, height, path);
        }
    }

    // ------------------------------------------------------------------ draw operation

    private sealed class Op : ICustomDrawOperation
    {
        private readonly Frame _frame;

        public Op(Frame frame) => _frame = frame;

        public Rect Bounds => new(0, 0, _frame.Width, _frame.Height);

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null)
            {
                Disable("no Skia lease feature");
                return;
            }

            using var lease = feature.Lease();
            if (lease.GrContext is null)
            {
                // A software target (a snapshot to a bitmap) after the screen already drew on the GPU: skip only this one.
                if (!_gpuSeen) Disable("no GPU context (software rendering)");
                else if (!_warnedSoftware)
                {
                    _warnedSoftware = true;
                    AppLog.Write("3D renderer: a software render target skipped a pet (expected only for snapshots)");
                }
                return;
            }
            _gpuSeen = true;

            var effect = GetEffect();
            if (effect is null) return;

            DrawFrame(lease.SkCanvas, effect, _frame);
            TryRenderSheet(lease.GrContext, effect);
        }
    }

    private static bool _gpuSeen;
    private static bool _warnedSoftware;

    private static void Disable(string reason)
    {
        if (!_enabled) return;
        _enabled = false;
        AppLog.Write("3D renderer disabled, using vector renderer: " + reason);
    }

    /// <summary>Compiles the shader on a worker thread at start-up, so the first frame does not stall for it.</summary>
    public static void Prewarm() => Task.Run(() =>
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        GetEffect();
        AppLog.Write($"3D shader compiled in {sw.ElapsedMilliseconds} ms (ahead of the first frame)");
    });

    private static SKRuntimeEffect? GetEffect()
    {
        lock (Gate)
        {
            if (_compileTried) return _effect;
            _compileTried = true;
            _effect = SKRuntimeEffect.Create(Shader, out var errors);
            if (_effect is null)
            {
                AppLog.Write("SDF shader compile failed: " + errors);
                _enabled = false;
            }
            else
            {
                AppLog.Write("3D renderer active (SkSL ray-marched SDF on GPU)");
            }
            return _effect;
        }
    }

    private static void DrawFrame(SKCanvas canvas, SKRuntimeEffect effect, Frame f)
    {
        var scale = Math.Max(0.1f, Math.Abs(canvas.TotalMatrix.ScaleX));
        var uniforms = new SKRuntimeEffectUniforms(effect)
        {
            ["uCount"] = (float)f.Count,
            ["uPix"] = 1.2f / scale,
            ["uShadow"] = f.Shadow,
            ["uFur"] = f.Fur,
            ["uCurl"] = f.Curl,
            ["uClip"] = f.Clip,
            ["uToon"] = f.Toon,
            ["uLine"] = f.Line,
            ["uShade"] = f.Shade,
            ["uStripe"] = f.Stripe,
            ["uYaw"] = new[] { f.YawCos, f.YawSin },
            ["uSq"] = f.Sq,
            ["uFoot"] = new[] { f.FootX, f.FootY },
            ["uCam"] = new[] { PetRenderer.CameraSin, PetRenderer.CameraCos },
            ["uP"] = f.P,
            ["uQ"] = f.Q,
            ["uM"] = f.M,
            ["uX"] = f.X,
        };
        using var shader = effect.ToShader(false, uniforms);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(new SKRect(0, 0, f.Width, f.Height), paint);

        if (f.Whiskers.Length >= 5)
        {
            using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = f.WhiskerWidth, StrokeCap = SKStrokeCap.Round };
            for (var i = 0; i + 4 < f.Whiskers.Length; i += 5)
            {
                stroke.Color = new SKColor(250, 248, 242, (byte)(f.Whiskers[i + 4] * 220));
                var x1 = f.FootX + f.Whiskers[i];
                var y1 = f.FootY + f.Whiskers[i + 1];
                var x2 = f.FootX + f.Whiskers[i + 2];
                var y2 = f.FootY + f.Whiskers[i + 3];
                using var path = new SKPath();
                path.MoveTo(x1, y1);
                path.QuadTo((x1 + x2) / 2, (y1 + y2) / 2 - Math.Abs(x2 - x1) * 0.12f, x2, y2);
                canvas.DrawPath(path, stroke);
            }
        }
    }

    private static SKSurface? _sheetSurface;
    private static int _sheetIndex;
    private const int SheetCellsPerFrame = 4;

    /// <summary>
    /// Renders the debug sheet a few cells per frame. One huge draw makes the GPU driver time out and the
    /// process gets killed, so the work is spread over many frames.
    /// </summary>
    private static void TryRenderSheet(GRContext gr, SKRuntimeEffect effect)
    {
        List<(float X, float Y, Frame Frame)>? cells;
        (int W, int H, string Path) target;
        lock (Gate)
        {
            if (_pendingSheet is null || _sheetTarget is null) return;
            cells = _pendingSheet;
            target = _sheetTarget.Value;
        }

        try
        {
            if (_sheetSurface is null)
            {
                _sheetSurface = SKSurface.Create(gr, false, new SKImageInfo(target.W, target.H, SKColorType.Rgba8888, SKAlphaType.Premul));
                _sheetSurface.Canvas.Clear(new SKColor(0x3C, 0x78, 0x3C));
                _sheetIndex = 0;
            }

            var c = _sheetSurface.Canvas;
            var end = Math.Min(cells.Count, _sheetIndex + SheetCellsPerFrame);
            for (; _sheetIndex < end; _sheetIndex++)
            {
                var (x, y, frame) = cells[_sheetIndex];
                c.Save();
                c.Translate(x, y);
                if (frame.Zoom != 1f) c.Scale(frame.Zoom);
                DrawFrame(c, effect, frame);
                c.Restore();
            }
            c.Flush();
            if (_sheetIndex < cells.Count) return;

            using (var image = _sheetSurface.Snapshot())
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var file = File.Create(target.Path))
                data.SaveTo(file);
            AppLog.Write("3D pose sheet saved: " + target.Path);
            Finish();
        }
        catch (Exception ex)
        {
            AppLog.Write("3D pose sheet failed: " + ex.Message);
            Finish();
        }

        void Finish()
        {
            _sheetSurface?.Dispose();
            _sheetSurface = null;
            lock (Gate)
            {
                _pendingSheet = null;
                _sheetTarget = null;
            }
        }
    }

    // ------------------------------------------------------------------ shader

    private const string Shader = @"
uniform float uCount;
uniform float uPix;
uniform float uShadow;
uniform float uFur;
uniform float uCurl;
uniform float uClip;
uniform float uToon;
uniform float3 uLine;
uniform float3 uShade;
uniform float3 uStripe;
uniform float2 uFoot;
uniform float2 uCam;
uniform float2 uYaw;
uniform float3 uSq;
uniform float4 uP[96];
uniform float4 uQ[96];
uniform float4 uM[96];
uniform float4 uX[96];

float sdRoundCone(float3 p, float3 a, float3 b, float r1, float r2) {
    float3 ba = b - a;
    float l2 = dot(ba, ba);
    if (l2 < 0.0001) { return length(p - a) - r1; }
    float rr = r1 - r2;
    float a2 = l2 - rr * rr;
    if (a2 <= 0.0001) { return min(length(p - a) - r1, length(p - b) - r2); }
    float il2 = 1.0 / l2;
    float3 pa = p - a;
    float y = dot(pa, ba);
    float z = y - l2;
    float3 xv = pa * l2 - ba * y;
    float x2 = dot(xv, xv);
    float y2 = y * y * l2;
    float z2 = z * z * l2;
    float k = sign(rr) * rr * rr * x2;
    if (sign(z) * a2 * z2 > k) { return sqrt(x2 + z2) * il2 - r2; }
    if (sign(y) * a2 * y2 < k) { return sqrt(x2 + y2) * il2 - r1; }
    return (sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
}

float sstep(float a, float b, float x) {
    float t = clamp((x - a) / (b - a), 0.0, 1.0);
    return t * t * (3.0 - 2.0 * t);
}

// Eyes are spheres clipped to the head sphere, so they sit flush in the face instead of bulging out.
float primDist(float3 p, float4 a, float4 b, float4 x) {
    if (x.x < -0.5) {
        return max(length(p - a.xyz) - a.w, length(p - b.xyz) - b.w);
    }
    return sdRoundCone(p, a.xyz, b.xyz, a.w, b.w);
}

float hash(float3 p) {
    p = fract(p * 0.3183099 + float3(0.11, 0.17, 0.13));
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(float3 x) {
    float3 i = floor(x);
    float3 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + float3(1.0, 0.0, 0.0));
    float c = hash(i + float3(0.0, 1.0, 0.0));
    float d = hash(i + float3(1.0, 1.0, 0.0));
    float e = hash(i + float3(0.0, 0.0, 1.0));
    float g = hash(i + float3(1.0, 0.0, 1.0));
    float h = hash(i + float3(0.0, 1.0, 1.0));
    float k = hash(i + float3(1.0, 1.0, 1.0));
    return mix(mix(mix(a, b, f.x), mix(c, d, f.x), f.y), mix(mix(e, g, f.x), mix(h, k, f.x), f.y), f.z);
}

// World (screen-aligned) -> body space, so fur and stripes stick to the animal when it turns.
// Cartoon eye: a coloured iris that brightens toward the bottom, a big round dark pupil, a dark lid line
// along the top and two white catchlights.
float3 toonEye(float3 p, float3 hc, float er, float4 head, float3 ef, float3 iris) {
    float3 en = normalize(p - hc);
    // The eye is a ball sunk into the head; only a cap of it shows. Lay the eye out on that cap, so the outline is
    // even all round however deep the ball sits: e = 0 at the rim of the visible cap, 1 at its middle.
    float3 hd = hc - head.xyz;
    float dist = max(length(hd), 0.001);
    float3 ca = hd / dist;
    float cb = clamp((head.w * head.w - dist * dist - er * er) / (2.0 * dist * er), -0.9, 0.95);
    float e = clamp((dot(en, ca) - cb) / (1.0 - cb), 0.0, 1.0);
    // Eye-space axes on the cap: across (x) and up (y).
    float3 rt = normalize(cross(float3(0.0, 1.0, 0.0), ca));
    float3 upv = cross(ca, rt);
    float span = sqrt(max(1.0 - cb * cb, 0.01));
    float y = dot(en, upv) / span;
    float3 dark = float3(0.07, 0.05, 0.07);
    // Iris: deep at the top, clear and glowing toward the bottom.
    float3 col = mix(iris * 0.5, min(iris * 1.35 + float3(0.12), float3(1.0)), sstep(0.45, -0.55, y));
    col = mix(col, min(iris * 1.8 + float3(0.28), float3(1.0)), sstep(-0.2, -0.6, y) * sstep(0.25, 0.55, e) * 0.5);
    // Darker ring round the edge of the iris.
    col = mix(col, iris * 0.32, sstep(0.32, 0.18, e) * 0.85);
    // Pupil: a tall rounded oval in the middle, nudged toward where the pet looks.
    float3 pc = normalize(ca * 0.9 + ef * 0.1);
    float3 prt = normalize(cross(float3(0.0, 1.0, 0.0), pc));
    float3 pup = cross(pc, prt);
    float px = dot(en, prt) / span;
    float py = dot(en, pup) / span;
    float pr = (px * px) / (0.34 * 0.34) + (py * py) / (0.5 * 0.5);
    col = mix(col, dark, sstep(1.1, 0.9, pr));
    // Outline: thin all round, a thicker lash line along the top.
    float lid = 0.09 + 0.16 * sstep(0.1, 0.75, y);
    col = mix(col, dark, sstep(lid + 0.04, lid, e));
    // Catchlights: a big one up on one side, a small one low on the other.
    float h1 = dot(en, normalize(ca + upv * 0.42 - rt * 0.36));
    float h2 = dot(en, normalize(ca - upv * 0.3 + rt * 0.3));
    col = mix(col, float3(1.0), sstep(0.955, 0.962, h1));
    col = mix(col, float3(1.0), sstep(0.985, 0.989, h2));
    return col;
}

float3 toModel(float3 p) {
    return float3(p.x * uYaw.x + p.z * uYaw.y, p.y, -p.x * uYaw.y + p.z * uYaw.x);
}

// Cartoon squash and stretch: undo the body-space scale (anchored at the feet) before sampling the rig.
float3 unsquash(float3 p) {
    float3 m = toModel(p) / uSq;
    return float3(m.x * uYaw.x - m.z * uYaw.y, m.y, m.x * uYaw.y + m.z * uYaw.x);
}

// Curly coat (Poodle, Bichon): round bumps packed over the body, wobbled by noise so they don't line up in rows.
float curlDisp(float3 q) {
    if (uCurl <= 0.0) { return 0.0; }
    float3 w = q * 0.68 + float3(noise(q * 0.31) * 2.2, noise(q * 0.31 + float3(5.0, 1.0, 3.0)) * 2.2, noise(q * 0.31 + float3(2.0, 7.0, 4.0)) * 2.2);
    float b = sin(w.x) * sin(w.y) * sin(w.z);
    return uCurl * (0.5 + 0.5 * b);
}

// Fine soft coat: small, dense tufts.
float furDisp(float3 q) {
    float n1 = noise(q * 0.55);
    float n2 = noise(q * 1.45 + float3(7.0, 3.0, 1.0));
    return uFur * (0.55 * n1 + 0.45 * n2 - 0.3);
}

// Coat displacement: curls fade out within a few units of the hard parts (eyes, nose) so they never cover the face.
float coatDisp(float3 q, float dh) {
    return furDisp(q) + curlDisp(q) * clamp((dh - 1.0) / 5.0, 0.0, 1.0);
}

float map(float3 p0) {
    float3 p = unsquash(p0);
    float sq = min(min(uSq.x, uSq.y), uSq.z);
    float ds = 100000.0;
    float dh = 100000.0;
    for (int i = 0; i < 96; i++) {
        if (float(i) >= uCount) { break; }
        float di = primDist(p, uP[i], uQ[i], uX[i]);
        float k = uM[i].w;
        if (k > 0.0) {
            float h = clamp(0.5 + 0.5 * (di - ds) / k, 0.0, 1.0);
            ds = mix(di, ds, h) - k * h * (1.0 - h);
        } else {
            dh = min(dh, di);
        }
    }
    return min(ds - coatDisp(toModel(p), dh), dh) * sq;
}

// Returns colour and a gloss value; gloss 2.0 marks an eye.
float4 material(float3 p0) {
    float3 p = unsquash(p0);
    float ds = 100000.0;
    float dh = 100000.0;
    float3 col = float3(0.5);
    float gl = 0.0;
    float st = 0.0;
    float3 hcol = float3(0.0);
    float4 hx = float4(0.0);
    float3 hc = float3(0.0);
    float hr = 0.0;
    float4 hq = float4(0.0);
    for (int i = 0; i < 96; i++) {
        if (float(i) >= uCount) { break; }
        float di = primDist(p, uP[i], uQ[i], uX[i]);
        float k = uM[i].w;
        if (k > 0.0) {
            float h = clamp(0.5 + 0.5 * (di - ds) / k, 0.0, 1.0);
            ds = mix(di, ds, h) - k * h * (1.0 - h);
            col = mix(uM[i].rgb, col, h);
            gl = mix(uX[i].x, gl, h);
            st = mix(uX[i].y, st, h);
        } else if (di < dh) {
            dh = di;
            hcol = uM[i].rgb;
            hx = uX[i];
            hc = uP[i].xyz;
            hr = uP[i].w;
            hq = uQ[i];
        }
    }

    float3 q = toModel(p);
    float fd = coatDisp(q, dh);
    if (dh < ds - fd) {
        if (hx.x < -0.5 && uToon > 0.5) {
            return float4(toonEye(p, hc, hr, hq, normalize(hx.yzw), hcol), 3.0);
        }
        if (hx.x < -0.5) {
            // Eye: sclera, iris with a dark limbal ring and radial fibres, big round pupil.
            float3 en = normalize(p - hc);
            float c = dot(en, normalize(hx.yzw));
            float lum = dot(hcol, float3(0.3, 0.59, 0.11));
            float edge = lum < 0.25 ? 0.25 : 0.55;
            float pupilEdge = lum < 0.25 ? 0.80 : 0.83;
            float irisT = sstep(edge, edge + 0.05, c);
            float rad = clamp((c - edge) / (1.0 - edge), 0.0, 1.0);
            float3 iris = hcol * (0.55 + 0.8 * rad);
            iris = mix(iris * 0.35, iris, sstep(edge, edge + 0.12, c));
            iris *= 0.85 + 0.3 * noise(en * 22.0);
            float3 e = mix(float3(0.97, 0.96, 0.94), iris, irisT);
            e = mix(e, float3(0.02, 0.015, 0.02), sstep(pupilEdge, pupilEdge + 0.03, c));
            return float4(e, 2.0);
        }
        return float4(hcol, hx.x);
    }

    // Soft tabby stripes in body space.
    if (st > 0.01) {
        float band = sin((q.x * 0.9 + q.y * 0.55) * 0.3 + 2.4 * noise(q * 0.1));
        if (uToon > 0.5) {
            float m = sstep(0.5, 0.58, band);
            col = mix(col, col * uStripe, m * st);
        } else {
            float m = sstep(0.4, 0.95, band) * (0.55 + 0.45 * noise(q * 0.45));
            col = mix(col, col * mix(float3(1.0), uStripe, 0.6), m * st);
        }
    }

    // Curls: shadowed hollows between the bumps, light on top of each curl.
    if (uCurl > 0.0) { col *= 0.8 + 0.28 * clamp(curlDisp(q) / uCurl, 0.0, 1.0); }

    // Fine strands: darker roots, lighter tips.
    float strand = 0.6 * noise(q * float3(2.4, 0.7, 2.4)) + 0.4 * noise(q * 4.0);
    float tip = clamp(fd / max(uFur, 0.01) + 0.3, 0.0, 1.0);
    if (uToon < 0.5) { col *= (0.9 + 0.16 * strand) * (0.9 + 0.14 * tip); }
    return float4(col, gl);
}

float3 calcNormal(float3 p) {
    float e = 0.5;
    float3 k1 = float3(1.0, -1.0, -1.0);
    float3 k2 = float3(-1.0, -1.0, 1.0);
    float3 k3 = float3(-1.0, 1.0, -1.0);
    float3 k4 = float3(1.0, 1.0, 1.0);
    return normalize(k1 * map(p + k1 * e) + k2 * map(p + k2 * e) + k3 * map(p + k3 * e) + k4 * map(p + k4 * e));
}

half4 main(float2 coord) {
    float sinP = uCam.x;
    float cosP = uCam.y;
    float sx = coord.x - uFoot.x;
    float sy = coord.y - uFoot.y;
    float3 v = float3(0.0, -sinP, -cosP);
    float3 o = float3(sx, -sy * cosP, sy * sinP) - v * 180.0;

    float t = 0.0;
    float minD = 100000.0;
    float tMin = 0.0;
    bool hit = false;
    for (int i = 0; i < 110; i++) {
        float3 p = o + v * t;
        // Swimming: everything below the water surface (y = 0) is hidden.
        if (uClip > 0.5 && p.y < 0.0) { break; }
        float d = map(p);
        if (d < minD) { minD = d; tMin = t; }
        if (d < 0.03) { hit = true; break; }
        t += d * 0.8;
        if (t > 420.0 || p.y < -6.0) { break; }
    }

    // Fuzzy silhouette: a thin, broken halo of fur around the body.
    float halo = uPix * 1.2 + uFur * 0.8;
    float alpha = 1.0;
    if (!hit) {
        float3 pe = o + v * tMin;
        float fz = noise(toModel(pe) * 1.8);
        alpha = clamp(1.0 - minD / halo, 0.0, 1.0) * clamp(0.45 + 0.8 * fz, 0.0, 1.0);
    }

    // Cartoon: a solid outline a couple of pixels wide around the silhouette instead of the fur halo.
    bool outlineOnly = false;
    if (uToon > 0.5 && !hit) {
        alpha = 1.0 - sstep(uPix * 1.8, uPix * 2.8, minD);
        outlineOnly = true;
    }

    float3 L = normalize(float3(-0.4, 0.8, 0.55));
    half4 outc = half4(0.0);

    if (alpha > 0.0 && outlineOnly) {
        outc = half4(half3(uLine * alpha), half(alpha));
    } else if (alpha > 0.0) {
        float3 p = o + v * (hit ? t : tMin);
        float3 n = calcNormal(p);
        float4 m = material(p);

        float occ = 0.0;
        float sca = 1.0;
        for (int j = 0; j < 4; j++) {
            float hh = 1.0 + 3.0 * float(j);
            float dd = map(p + n * hh);
            occ += (hh - dd) * sca;
            sca *= 0.7;
        }
        float ao = clamp(1.0 - occ * 0.06, 0.0, 1.0);

        float ndl = dot(n, L);
        float dif = clamp((ndl + 0.45) / 1.45, 0.0, 1.0);
        float sky = 0.6 + 0.4 * n.y;
        float rim = pow(clamp(1.0 + dot(n, v), 0.0, 1.0), 2.5);
        float3 hv = normalize(L - v);
        float3 col;

        if (m.w > 1.5) {
            // Wet, glossy eye with two catchlights.
            float spe = pow(clamp(dot(n, hv), 0.0, 1.0), 220.0) * 1.4;
            float c1 = sstep(0.982, 0.99, clamp(dot(n, normalize(normalize(float3(-0.45, 0.6, 0.65)) - v)), 0.0, 1.0));
            float c2 = sstep(0.994, 0.997, clamp(dot(n, normalize(normalize(float3(0.35, -0.25, 0.9)) - v)), 0.0, 1.0));
            col = m.rgb * (0.5 * sky + 0.65 * dif) * (0.45 + 0.55 * ao) + spe + c1 * 1.1 + c2 * 0.7;
        } else {
            float spe = pow(clamp(dot(n, hv), 0.0, 1.0), 48.0) * max(m.w, 0.0);
            float sss = clamp(1.0 - abs(ndl), 0.0, 1.0) * 0.16;
            col = m.rgb * (0.42 * sky * float3(0.95, 0.98, 1.05) + 0.72 * dif * float3(1.0, 0.96, 0.9)) * (0.4 + 0.6 * ao)
                + m.rgb * float3(1.0, 0.55, 0.42) * sss
                + rim * m.rgb * 0.55 * (0.5 + 0.5 * ao)
                + spe * 0.8;
        }

        if (uToon > 0.5) {
            if (m.w > 2.5) {
                col = m.rgb;                                   // cartoon eyes are flat-coloured
            } else {
                // Cel shading: lit, mid and dark bands with a warm shadow tint.
                float band = ndl > 0.12 ? 1.0 : (ndl > -0.4 ? 0.86 : 0.74);
                float3 tint = band > 0.99 ? float3(1.0) : uShade;
                col = m.rgb * band * tint * (0.9 + 0.1 * ao);
                col += sstep(0.95, 0.965, dot(n, hv)) * max(m.w, 0.0) * 0.5;      // shine on nose and tag
                col += sstep(0.62, 0.7, rim) * sstep(0.1, 0.4, n.y) * 0.07;       // soft rim light on top edges
            }
            // Inner contour where the surface turns away from the viewer.
            float edge = 1.0 - sstep(0.16, 0.28, -dot(n, v));
            col = mix(col, uLine, edge);
        }
        col = pow(clamp(col, 0.0, 1.0), float3(0.95));
        outc = half4(half3(col * alpha), half(alpha));
    }

    if (alpha < 1.0 && uShadow > 0.0) {
        float tg = o.y / sinP;
        float3 g = o + v * tg;
        float res = 1.0;
        float tt = 1.5;
        for (int k = 0; k < 24; k++) {
            float hh = map(g + L * tt);
            res = min(res, 5.0 * hh / tt);
            tt += clamp(hh, 1.0, 12.0);
            if (res < 0.02 || tt > 140.0) { break; }
        }
        res = clamp(res, 0.0, 1.0);
        float aoG = clamp(map(g) / 16.0, 0.0, 1.0);
        float sh = clamp((1.0 - res) * 0.32 + (1.0 - aoG) * 0.3, 0.0, 0.6) * uShadow;
        outc.a = outc.a + half(sh * (1.0 - alpha));
    }
    return outc;
}
";
}
