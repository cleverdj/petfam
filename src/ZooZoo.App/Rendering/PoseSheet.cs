using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ZooZoo.Core.Geometry;
using ZooZoo.Core.Pets;

namespace ZooZoo.Desktop.Rendering;

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
        string[] poses = { "idle", "walk", "sit", "lie", "sleep" };
        float[] headings = { 0f, 30f, 60f, 90f, 120f, 150f, 180f, 225f, 270f, 315f };
        var now = DateTime.UtcNow;
        var rng = new Random(1);
        var cells = new List<(float X, float Y, SdfPetRenderer.Frame Frame)>();
        for (var row = 0; row < poses.Length; row++)
        for (var col = 0; col < headings.Length; col++)
        {
            var pet = new Pet(species, "turn", now, rng) { Position = Vec2.Zero, Heading = headings[col], TargetHeading = headings[col] };
            pet.SetAnimation(poses[row]);
            pet.AnimationTime = 0.37f;
            var frame = SdfPetRenderer.Build(pet, 1.6f, CellW / 2f * 1.6f, (CellH - 30) * 1.6f, CellW * 1.6f, CellH * 1.6f);
            cells.Add((col * CellW * 1.6f, row * CellH * 1.6f, frame));
        }
        SdfPetRenderer.QueueSheet(cells, (int)(CellW * 1.6f * headings.Length), (int)(CellH * 1.6f * poses.Length), path);
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
