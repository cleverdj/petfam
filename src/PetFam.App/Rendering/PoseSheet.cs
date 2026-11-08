using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;

namespace PetFam.Desktop.Rendering;

/// <summary>Renders every pose for both species at several headings into one PNG, for checking the rig quickly.</summary>
public static class PoseSheet
{
    private static readonly string[] Poses =
    {
        "idle", "walk", "run", "sit", "lie", "sleep", "eat", "stretch", "shake", "groom", "dig", "petted", "carried", "swim",
        "stalk", "wiggle", "pounce", "bat", "playbow", "hiss", "wrestle", "box", "startle", "submit", "standover",
    };

    private static readonly (PetSpecies Species, float Heading, string Label)[] Rows =
    {
        (PetSpecies.Cat, 0f, "cat 0° (side)"),
        (PetSpecies.Dog, 0f, "dog 0° (side)"),
        (PetSpecies.Cat, 60f, "cat 60° (3/4 front)"),
        (PetSpecies.Dog, 90f, "dog 90° (front)"),
        (PetSpecies.Cat, 100f, "cat 100° (front)"),
        (PetSpecies.Dog, 270f, "dog 270° (back)"),
    };

    private const int CellW = 190;
    private const int CellH = 210;

    /// <summary>Queues the same sheet for the GPU 3D renderer; it is written on the next GPU frame.</summary>
    public static void QueueSdfSheet(string path)
    {
        var now = DateTime.UtcNow;
        var rng = new Random(1);
        var cells = new List<(float X, float Y, SdfPetRenderer.Frame Frame)>();
        for (var row = 0; row < Rows.Length; row++)
        {
            var (species, heading, _) = Rows[row];
            for (var col = 0; col < Poses.Length; col++)
            {
                var pet = new Pet(species, "sheet", now, rng) { Position = Vec2.Zero, Heading = heading, TargetHeading = heading };
                pet.SetAnimation(Poses[col]);
                pet.AnimationTime = 0.37f;
                if (Poses[col] == "shake") pet.Appearance.EnterWater(0.8f);
                if (Poses[col] == "dig") pet.Appearance.AddMud(0.8f);
                pet.LookAt = Poses[col] == "idle" ? new Vec2(60, 40) : null;
                var frame = SdfPetRenderer.Build(pet, 1f, CellW / 2f, CellH - 30, CellW, CellH);
                cells.Add((col * CellW, row * CellH, frame));
            }
        }
        SdfPetRenderer.QueueSheet(cells, CellW * Poses.Length, CellH * Rows.Length, path);
    }

    /// <summary>Turntable sheet: rows = poses, columns = headings. For judging how a pet looks from every side.</summary>
    public static void QueueTurnSheet(string path, PetSpecies species)
    {
        // PETFAM_TURN_POSES=pant,yawn,sniff picks other poses to check.
        var poses = Environment.GetEnvironmentVariable("PETFAM_TURN_POSES") is { Length: > 0 } list
            ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : new[] { "idle", "walk", "sit", "lie", "sleep" };
        float[] headings = { 0f, 30f, 60f, 90f, 120f, 150f, 180f, 225f, 270f, 315f };
        var now = DateTime.UtcNow;
        var rng = new Random(1);
        var cells = new List<(float X, float Y, SdfPetRenderer.Frame Frame)>();
        for (var row = 0; row < poses.Length; row++)
        for (var col = 0; col < headings.Length; col++)
        {
            var pet = new Pet(species, "turn", now, rng) { Position = Vec2.Zero, Heading = headings[col], TargetHeading = headings[col] };
            pet.Growth = new Growth(now, Growth.AdultAt);
            pet.SetAnimation(poses[row]);
            pet.AnimationTime = 0.37f;
            var frame = SdfPetRenderer.Build(pet, 1.6f, CellW / 2f * 1.6f, (CellH - 30) * 1.6f, CellW * 1.6f, CellH * 1.6f);
            cells.Add((col * CellW * 1.6f, row * CellH * 1.6f, frame));
        }
        SdfPetRenderer.QueueSheet(cells, (int)(CellW * 1.6f * headings.Length), (int)(CellH * 1.6f * poses.Length), path);
    }

    /// <summary>Growth sheet: rows = cat and dog, columns = days together, idle and seen from the side-front.</summary>
    public static void QueueGrowthSheet(string path)
    {
        int[] days = { 0, 7, 15, 30, 46, 200, 400, 500 };
        var now = DateTime.UtcNow;
        var cells = new List<(float X, float Y, SdfPetRenderer.Frame Frame)>();
        var species = new[] { PetSpecies.Cat, PetSpecies.Dog };
        for (var row = 0; row < species.Length; row++)
        for (var col = 0; col < days.Length; col++)
        {
            var pet = new Pet(species[row], "grow", now, new Random(1)) { Position = Vec2.Zero, Heading = 30f, TargetHeading = 30f };
            pet.Growth = new Growth(now, days[col]);
            pet.SetAnimation("idle");
            pet.AnimationTime = 0.37f;
            var frame = SdfPetRenderer.Build(pet, 1.6f, CellW / 2f * 1.6f, (CellH - 30) * 1.6f, CellW * 1.6f, CellH * 1.6f);
            cells.Add((col * CellW * 1.6f, row * CellH * 1.6f, frame));
        }
        SdfPetRenderer.QueueSheet(cells, (int)(CellW * 1.6f * days.Length), (int)(CellH * 1.6f * species.Length), path);
    }

    /// <summary>
    /// Close-ups, big enough to judge faces and coats: the breeds in PETFAM_CLOSEUP_BREEDS (ids, comma separated;
    /// default: poodle, bichon and a few cats), three-quarter view and facing you.
    /// </summary>
    public static void QueueCloseUpSheet(string path)
    {
        // "all" (or nothing): every breed. Four breeds to a row, each three-quarter view and facing you.
        var list = Environment.GetEnvironmentVariable("PETFAM_CLOSEUP_BREEDS");
        var ids = list is { Length: > 0 } && list != "all"
            ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Breed.All.Select(b => b.Id).ToArray();
        var now = DateTime.UtcNow;
        var s = float.TryParse(Environment.GetEnvironmentVariable("PETFAM_CLOSEUP_ZOOM"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var z) ? z : 3f;
        const int perRow = 4;
        const float cw = CellW, ch = CellH - 40;
        var cells = new List<(float X, float Y, SdfPetRenderer.Frame Frame)>();
        var n = 0;
        foreach (var id in ids)
        {
            if (Breed.All.FirstOrDefault(b => b.Id == id) is not { } breed) continue;
            var (row, slot) = (n / perRow, n % perRow);
            var col = 0;
            foreach (var heading in new[] { 40f, 90f })
            {
                var pet = new Pet(breed.Species, breed.Name, now, new Random(1), breed) { Position = Vec2.Zero, Heading = heading, TargetHeading = heading };
                pet.Growth = new Growth(now, Growth.AdultAt);
                pet.SetAnimation("idle");
                pet.AnimationTime = 0.9f;                       // eyes open (0.37 catches some breeds mid-blink)
                var frame = SdfPetRenderer.Build(pet, 1f, cw / 2f, ch - 10, cw, ch);
                frame.Zoom = s;
                cells.Add(((slot * 2 + col) * cw * s, row * ch * s, frame));
                col++;
            }
            n++;
        }
        var rows = (n + perRow - 1) / perRow;
        SdfPetRenderer.QueueSheet(cells, (int)(cw * s * Math.Min(n, perRow) * 2), (int)(ch * s * rows), path);
    }

    /// <summary>Every breed as an adult (rows: dogs, then cats), three-quarter view and facing you.</summary>
    public static void QueueBreedSheet(string path)
    {
        var now = DateTime.UtcNow;
        const float S = 1.6f;
        var cells = new List<(float X, float Y, SdfPetRenderer.Frame Frame)>();
        var rows = new[] { PetSpecies.Dog, PetSpecies.Cat };
        var cols = 0;
        for (var row = 0; row < rows.Length; row++)
        {
            var col = 0;
            foreach (var breed in Breed.Of(rows[row]))
            foreach (var heading in new[] { 35f, 90f })
            {
                var pet = new Pet(breed.Species, breed.Name, now, new Random(1), breed) { Position = Vec2.Zero, Heading = heading, TargetHeading = heading };
                pet.Growth = new Growth(now, Growth.AdultAt);
                pet.SetAnimation("idle");
                pet.AnimationTime = 0.37f;
                var frame = SdfPetRenderer.Build(pet, S, CellW / 2f * S, (CellH - 30) * S, CellW * S, CellH * S);
                cells.Add((col * CellW * S, row * CellH * S, frame));
                col++;
            }
            cols = Math.Max(cols, col);
        }
        SdfPetRenderer.QueueSheet(cells, (int)(CellW * S * cols), (int)(CellH * S * rows.Length), path);
    }

    /// <summary>
    /// Motion filmstrip: a pet driven through a scripted move at 30 fps, every third frame drawn left to right.
    /// It starts facing away, turns round, speeds up, walks, brakes, sits, gets up and runs.
    /// For judging transitions, which single-pose sheets cannot show.
    /// </summary>
    public static void QueueMotionSheet(string path, PetSpecies species)
    {
        const float dt = 1f / 30f;
        const int every = 3, cols = 10;
        var pet = new Pet(species, "motion", DateTime.UtcNow, new Random(1)) { Position = Vec2.Zero, Heading = 270f, TargetHeading = 270f };
        pet.Growth = new Growth(DateTime.UtcNow, Growth.AdultAt);
        var cruise = species.WalkSpeed();
        var steps = new List<(string Kind, Vec2 Target, float Seconds)>
        {
            ("idle", default, 0.4f),
            ("walk", new Vec2(-170, 100), 0),
            ("idle", default, 0.5f),
            ("sit", default, 1.2f),
            ("idle", default, 0.4f),
            ("run", new Vec2(160, 120), 0),
            ("idle", default, 0.6f),
            ("startle", default, 0.8f),
            ("idle", default, 0.4f),
            ("pounce", default, 0.9f),
            ("idle", default, 0.6f),
        };

        // Cartoon effects (dust, emotes) are 2D overlays: drawn into a matching transparent sheet next to the 3D one.
        var fxW = (int)(CellW * 1.2f * cols);
        var fxH = (int)(CellH * 1.2f * 12);
        var fxSheet = new RenderTargetBitmap(new PixelSize(fxW, fxH));
        var fxCtx = fxSheet.CreateDrawingContext();

        var cells = new List<(float X, float Y, SdfPetRenderer.Frame Frame)>();
        var frame = 0;
        PetRenderer.FixedDt = dt;
        try
        {
            foreach (var (kind, target, seconds) in steps)
            {
                var time = 0f;
                while (true)
                {
                    if (kind is "walk" or "run")
                    {
                        var delta = target - pet.Position;
                        if (delta.Length <= 1f) break;
                        var speed = kind == "run" ? cruise * species.RunMultiplier() : cruise;
                        var step = pet.Motion.Step(pet, delta, speed, dt);
                        pet.Position += delta.Normalized * step;
                        pet.FaceToward(delta);
                        pet.SetAnimation(kind);
                    }
                    else
                    {
                        if (time >= seconds) break;
                        pet.SetAnimation(kind);
                        pet.Lift = kind switch
                        {
                            "startle" when time < 0.4f => MathF.Sin(time / 0.4f * MathF.PI) * 24f,
                            "pounce" when time is > 0.15f and < 0.65f => MathF.Sin((time - 0.15f) / 0.5f * MathF.PI) * 45f,
                            _ => 0f,
                        };
                        if (kind == "pounce") pet.Position += new Vec2(time is > 0.15f and < 0.65f ? 110f * dt : 0f, 0);
                    }
                    time += dt;
                    pet.AnimationTime += dt;
                    PetFam.Core.Simulation.TurnToward(pet, dt);
                    pet.Motion.EndFrame(pet, dt, 1f);

                    var built = SdfPetRenderer.Build(pet, 1.2f, CellW / 2f * 1.2f, (CellH - 30) * 1.2f, CellW * 1.2f, CellH * 1.2f);
                    if (frame % every == 0)
                    {
                        var n = cells.Count;
                        cells.Add((n % cols * CellW * 1.2f, n / cols * CellH * 1.2f, built));
                        if (n < cols * 12)
                            PetRenderer.DrawOverlays(fxCtx, pet,
                                new Vec2(n % cols * CellW * 1.2f + CellW / 2f * 1.2f, n / cols * CellH * 1.2f + (CellH - 30) * 1.2f));
                    }
                    frame++;
                    if (time > 20f) break;
                }
            }
        }
        finally
        {
            PetRenderer.FixedDt = null;
            fxCtx.Dispose();
        }
        fxSheet.Save(Path.ChangeExtension(path, ".fx.png"));
        var rows = (cells.Count + cols - 1) / cols;
        SdfPetRenderer.QueueSheet(cells, (int)(CellW * 1.2f * cols), (int)(CellH * 1.2f * rows), path);
        AppLog.Write($"Motion sheet: {frame} frames, {cells.Count} drawn");
    }

    public static void Save(string path)
    {
        var control = new SheetControl();
        var size = new Size(CellW * Poses.Length, CellH * Rows.Length);
        control.Measure(size);
        control.Arrange(new Rect(size));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
        rtb.Render(control);
        rtb.Save(path);
    }

    private sealed class SheetControl : Control
    {
        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0x3C, 0x78, 0x3C)), new Rect(Bounds.Size));
            var now = DateTime.UtcNow;
            var rng = new Random(1);
            var label = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));

            for (var row = 0; row < Rows.Length; row++)
            {
                var (species, heading, rowLabel) = Rows[row];
                for (var col = 0; col < Poses.Length; col++)
                {
                    var pet = new Pet(species, "sheet", now, rng) { Position = Vec2.Zero, Heading = heading, TargetHeading = heading };
                    pet.SetAnimation(Poses[col]);
                    pet.AnimationTime = 0.37f;
                    if (Poses[col] == "shake") pet.Appearance.EnterWater(0.8f);
                    if (Poses[col] == "dig") pet.Appearance.AddMud(0.8f);
                    pet.LookAt = Poses[col] == "idle" ? new Vec2(60, 40) : null;

                    var origin = new Vec2(col * CellW + CellW / 2f, row * CellH + CellH - 30);
                    ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), 1), new Point(col * CellW, origin.Y), new Point(col * CellW + CellW, origin.Y));
                    PetRenderer.DrawBody(ctx, pet, origin, 1f);

                    var text = new FormattedText(col == 0 ? $"{Poses[col]}  [{rowLabel}]" : Poses[col],
                        System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 13, label);
                    ctx.DrawText(text, new Point(col * CellW + 6, row * CellH + 6));
                }
            }
        }
    }
}
