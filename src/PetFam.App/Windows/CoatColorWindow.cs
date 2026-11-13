using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PetFam.Core;
using PetFam.Core.Pets;
using PetFam.Desktop.Rendering;

namespace PetFam.Desktop.Windows;

/// <summary>
/// Coat colour editor: pick a pet's coat, light parts (belly, chest, paws), markings and eye colour from suggested
/// swatches or any colour at all, with a live preview of the pet. "Breed colours" puts everything back. Works on a
/// copy; Save hands the picks back.
/// </summary>
public sealed class CoatColorWindow : Window
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x40));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x70, 0x7A, 0x8A));

    private enum Part { Fur, Light, Marking, Eyes }

    /// <summary>Suggestions per part: natural colours first, then a few just-for-fun ones.</summary>
    private static readonly Dictionary<Part, uint[]> Swatches = new()
    {
        [Part.Fur] = new uint[]
        {
            0xF6F4EE, 0xF2DEB8, 0xEBB57A, 0xE0A85A, 0xEA953C, 0xC8622A, 0x8A5A36, 0x5A3A26, 0x2A2626,
            0x8E949E, 0xBDC3CC, 0x7F8A99, 0xE3C79A, 0xF2A9C4, 0x9FDCC4, 0x9CC8F0, 0xC4B0E8, 0xF4D86A,
        },
        [Part.Light] = new uint[] { 0xFFFFFF, 0xFFF4E2, 0xF2E2C4, 0xEED8B4, 0xE6E8EC, 0xFCE0E8, 0xE4F4EC, 0xE4EEFC },
        [Part.Marking] = new uint[] { 0x2A2626, 0x4A3220, 0x7A5A3A, 0x5A4034, 0x4E545E, 0xC8783A, 0x9A6A50, 0xF2F0EA, 0xD86A9A, 0x5A7AC8 },
        [Part.Eyes] = new uint[] { 0x3A2214, 0x6A4424, 0xD9A030, 0xD98A1C, 0x5A9A3A, 0x4E7A34, 0x4F86D6, 0x3FA3B8, 0x9A6AD8, 0x2A1A10 },
    };

    private readonly Pet _preview;
    private readonly PetPreview _view;
    private readonly DispatcherTimer _timer;
    private uint? _fur, _light, _marking, _eyes;
    private readonly List<(Border Swatch, Part Part, uint? Rgb)> _swatches = new();
    private readonly Dictionary<Part, Border> _custom = new();

    public CoatColorWindow(Pet pet, Action<PetCoat?> onSave)
    {
        (_fur, _light, _marking, _eyes) = (pet.Coat?.Fur, pet.Coat?.Light, pet.Coat?.Marking, pet.Coat?.Eyes);
        Title = L.S($"{pet.Name}'s colours", $"{pet.Name} 색 바꾸기");
        Width = 700;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = PieMenuWindow.UiFont;
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFC));

        // A stand-in with the same breed and age, so the real pet only changes on Save.
        _preview = new Pet(pet.Species, pet.Name, DateTime.UtcNow, new Random(1), pet.Breed) { Growth = pet.Growth, Heading = 40f, TargetHeading = 40f };
        _preview.SetAnimation("sit");
        _view = new PetPreview(_preview) { Width = 260, Height = 240 };
        var turn = 40f;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) =>
        {
            _preview.AnimationTime += 0.05f;
            _view.InvalidateVisual();
        });
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        // Drag the preview to turn the pet round.
        Point? drag = null;
        _view.PointerPressed += (_, e) => drag = e.GetPosition(_view);
        _view.PointerReleased += (_, _) => drag = null;
        _view.PointerMoved += (_, e) =>
        {
            if (drag is not { } d) return;
            var p = e.GetPosition(_view);
            turn = (turn - (float)(p.X - d.X) * 0.8f + 360f) % 360f;
            _preview.Heading = _preview.TargetHeading = turn;
            drag = p;
        };
        _view.Cursor = new Cursor(StandardCursorType.SizeWestEast);

        var rows = new StackPanel { Spacing = 14 };
        rows.Children.Add(Row(Part.Fur, L.S("Coat", "털 색")));
        rows.Children.Add(Row(Part.Light, L.S("Belly, chest and paws", "배·가슴·발 색")));
        rows.Children.Add(Row(Part.Marking, L.S("Markings (stripes, spots, back, points)", "무늬 색 (줄무늬·점·등·포인트)")));
        rows.Children.Add(Row(Part.Eyes, L.S("Eyes", "눈 색")));

        var reset = new Button { Content = L.S("Breed colours", "품종 원래 색으로"), Padding = new Thickness(14, 8), CornerRadius = new CornerRadius(10) };
        reset.Click += (_, _) => { _fur = _light = _marking = _eyes = null; Refresh(); };
        var cancel = new Button { Content = L.S("Cancel", "취소"), Padding = new Thickness(14, 8), CornerRadius = new CornerRadius(10) };
        cancel.Click += (_, _) => Close();
        var save = new Button
        {
            Content = L.S("Save", "저장"), Background = PieMenuWindow.SimsBlue, Foreground = Brushes.White, FontWeight = FontWeight.Bold,
            Padding = new Thickness(18, 8), CornerRadius = new CornerRadius(10),
        };
        save.Click += (_, _) => { onSave(Current.OrNull()); Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { reset, cancel, save } };

        var hint = new TextBlock
        {
            Text = L.S("Pick a suggested colour or click ＋ for any colour. Drag the preview to turn your pet round.",
                "추천 색을 고르거나 ＋를 눌러 원하는 색을 직접 고르세요. 미리보기를 끌면 펫이 돌아가요."),
            Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Width = 260,
        };
        var left = new StackPanel { Spacing = 8, Children = { _view, hint } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(18) };
        grid.Children.Add(left);
        var right = new StackPanel { Spacing = 16, Margin = new Thickness(18, 0, 0, 0), Children = { rows, buttons } };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;
        Refresh();
    }

    private PetCoat Current => new(_fur, _light, _marking, _eyes);

    private uint? Get(Part p) => p switch { Part.Fur => _fur, Part.Light => _light, Part.Marking => _marking, _ => _eyes };

    private void Set(Part p, uint? rgb)
    {
        switch (p)
        {
            case Part.Fur: _fur = rgb; break;
            case Part.Light: _light = rgb; break;
            case Part.Marking: _marking = rgb; break;
            default: _eyes = rgb; break;
        }
        Refresh();
    }

    private Control Row(Part part, string label)
    {
        var wrap = new WrapPanel();
        wrap.Children.Add(Swatch(part, null));                     // the breed's own colour
        foreach (var rgb in Swatches[part]) wrap.Children.Add(Swatch(part, rgb));

        // ＋: any colour at all.
        var custom = new Border
        {
            Width = 28, Height = 28, Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(3), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "＋", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 14, Foreground = Ink },
        };
        ToolTip.SetTip(custom, L.S("Any colour…", "직접 고르기…"));
        var picker = new ColorView
        {
            Width = 320, Height = 330, IsAlphaEnabled = false, IsAlphaVisible = false,
            IsColorPaletteVisible = false, IsComponentTextInputVisible = true, IsHexInputVisible = true,
        };
        var flyout = new Flyout { Content = picker, Placement = PlacementMode.Bottom };
        custom.PointerPressed += (_, e) =>
        {
            var c = BreedLook.C(Get(part) ?? 0xC0C0C0);
            picker.Color = c;
            flyout.ShowAt(custom);
            e.Handled = true;
        };
        picker.ColorChanged += (_, e) =>
        {
            var c = e.NewColor;
            Set(part, (uint)(c.R << 16 | c.G << 8 | c.B));
        };
        _custom[part] = custom;
        wrap.Children.Add(custom);

        return new StackPanel
        {
            Spacing = 6,
            Children = { new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Ink }, wrap },
        };
    }

    private Border Swatch(Part part, uint? rgb)
    {
        var sw = new Border
        {
            Width = 28, Height = 28, Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(3), Cursor = new Cursor(StandardCursorType.Hand),
        };
        if (rgb is { } c) sw.Background = new SolidColorBrush(BreedLook.C(c));
        else
        {
            // The breed's own colour: half the swatch in it, with a little "auto" mark.
            sw.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            sw.Child = new TextBlock { Text = "★", FontSize = 12, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(sw, L.S("The breed's colour", "품종 원래 색"));
        }
        sw.PointerPressed += (_, e) => { Set(part, rgb); e.Handled = true; };
        _swatches.Add((sw, part, rgb));
        return sw;
    }

    private void Refresh()
    {
        _preview.Coat = Current.OrNull();
        foreach (var (sw, part, rgb) in _swatches)
            sw.BorderBrush = Get(part) == rgb ? PieMenuWindow.SimsBlue : new SolidColorBrush(Color.FromRgb(0xD6, 0xDD, 0xE8));
        foreach (var (part, custom) in _custom)
        {
            var picked = Get(part) is { } v && !Swatches[part].Contains(v);
            custom.BorderBrush = picked ? PieMenuWindow.SimsBlue : new SolidColorBrush(Color.FromRgb(0xD6, 0xDD, 0xE8));
            custom.Background = picked ? new SolidColorBrush(BreedLook.C(Get(part)!.Value)) : Brushes.White;
        }
        _view.InvalidateVisual();
    }
}
