using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PetFam.Core.Geometry;
using PetFam.Core.Pets;
using PetFam.Core.World;

namespace PetFam.Desktop.Rendering;

/// <summary>
/// Test option PETFAM_DOOR_SHEET=1: draws, off screen, a pet walking into a house step by step, a house with a pet
/// asleep inside, and cats and dogs peeking from every window edge, into one PNG. Uses the vector renderer (the GPU
/// renderer only draws to the screen); the shapes, clipping and tilt are the same.
/// </summary>
public static class SceneSheet
{
    public static void Save(string path)
    {
        const int W = 1500, H = 760;
        var now = DateTime.UtcNow;
        var bounds = new RectI(0, 0, W, H);
        var cell = new Cell
        {
            Id = "sheet", DesktopId = "sheet", MonitorId = "sheet", Bounds = bounds, WorkingArea = bounds,
            Map = HabitatMap.Flat(bounds, bounds),
        };
        var canvas = new Canvas { Width = W, Height = H, Background = new SolidColorBrush(Color.FromRgb(0xCF, 0xE8, 0xC4)) };

        // Row 1: a cat lining up at the door, stepping in, half through, and asleep inside.
        var steps = new[] { (Dy: 26f, Label: "At the door"), (Dy: 6f, Label: "On the threshold"), (Dy: -6f, Label: "Going in"), (Dy: -16f, Label: "Asleep inside") };
        for (var i = 0; i < steps.Length; i++)
        {
            var house = new PetHouse { Name = "Momo's", Position = new Vec2(190 + i * 360, 300), Decorations = new() { "flowers" } };
            var cat = new Pet(PetSpecies.Cat, "Momo", now, new Random(3)) { Growth = new Growth(now, Growth.AdultAt) };
            cat.Position = new Vec2(house.Position.X, house.Position.Y + steps[i].Dy * cell.Map.DepthScale(house.Position.Y));
            cat.Heading = cat.TargetHeading = 270f;
            cat.SetAnimation("walk");
            var last = i == steps.Length - 1;
            if (last) { cat.Hidden = true; cat.InsideHouse = house.Id; cat.Heading = cat.TargetHeading = 90f; cat.SetAnimation("doze"); }
            else cat.Doorway = new Doorway { Door = house.Position, HouseDepth = cell.Map.DepthScale(house.Position.Y), HouseId = house.Id };

            var hs = new HouseSprite(house) { Occupied = last };
            if (last)
            {
                var c = PetRenderer.CoatOf(cat);
                hs.Occupants = new[] { new HouseArt.Occupant(cat.Name, c.Fur, c.Belly, c.Dark, c.Nose, c.EarInner, true) };
            }
            hs.Place(cell);
            canvas.Children.Add(hs);
            var ps = new PetSprite(cat);
            if (last) ps.Home = (house.Position, cell.Map.DepthScale(house.Position.Y));
            ps.Place(cell);
            canvas.Children.Add(ps);
            canvas.Children.Add(Label(steps[i].Label, house.Position.X, 330));
        }

        // Row 2: peeking from each edge of a window (grey), cat then dog.
        var edges = new[] { PeekEdge.Top, PeekEdge.Left, PeekEdge.Right, PeekEdge.Bottom };
        for (var j = 0; j < 8; j++)
        {
            var species = j < 4 ? PetSpecies.Cat : PetSpecies.Dog;
            var edge = edges[j % 4];
            var tileX = 20 + j * 184;
            var win = edge switch
            {
                PeekEdge.Top => new RectI(tileX + 20, 520, 140, 200),
                PeekEdge.Bottom => new RectI(tileX + 20, 400, 140, 200),
                PeekEdge.Left => new RectI(tileX + 80, 430, 100, 260),
                _ => new RectI(tileX, 430, 100, 260),
            };
            var anchor = edge switch
            {
                PeekEdge.Top => new Vec2(win.X + win.Width / 2f, win.Top),
                PeekEdge.Bottom => new Vec2(win.X + win.Width / 2f, win.Bottom),
                PeekEdge.Left => new Vec2(win.Left, win.Y + win.Height / 2f),
                _ => new Vec2(win.Right, win.Y + win.Height / 2f),
            };
            var pet = new Pet(species, "", now, new Random(4)) { Growth = new Growth(now, Growth.AdultAt) };
            pet.Peek = new PeekState { Edge = edge, Anchor = anchor, WindowRect = win, Progress = 1f };
            pet.Heading = pet.TargetHeading = 90f;
            pet.SetAnimation("peek");

            var window = new Border
            {
                Width = win.Width, Height = win.Height, Background = new SolidColorBrush(Color.FromRgb(0xE4, 0xE6, 0xEA)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAA)), BorderThickness = new Thickness(1),
            };
            Canvas.SetLeft(window, win.X);
            Canvas.SetTop(window, win.Y);
            canvas.Children.Add(window);
            var sprite = new PeekSprite(pet) { Cursor = new Vec2(anchor.X + 120, anchor.Y + 60) };
            sprite.Place(bounds);
            canvas.Children.Add(sprite);
        }

        SdfPetRenderer.Suspended = true;
        try
        {
            canvas.Measure(new Size(W, H));
            canvas.Arrange(new Rect(0, 0, W, H));
            using var rtb = new RenderTargetBitmap(new PixelSize(W, H), new Vector(96, 96));
            rtb.Render(canvas);   // first pass learns where each head is
            rtb.Render(canvas);
            rtb.Save(path);
        }
        finally
        {
            SdfPetRenderer.Suspended = false;
        }
    }

    private static Control Label(string text, double x, double y)
    {
        var t = new TextBlock { Text = text, FontSize = 14, Foreground = Brushes.Black, FontFamily = new FontFamily("Malgun Gothic") };
        Canvas.SetLeft(t, x - 40);
        Canvas.SetTop(t, y + 20);
        return t;
    }
}
