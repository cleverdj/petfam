using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PetFam.Core;
using PetFam.Core.Pets;
using PetFam.Desktop.Rendering;

namespace PetFam.Desktop.Windows;

/// <summary>
/// Dress-up: pick what the pet wears round its neck, on its head, on its body and on its face, each in a colour,
/// with a live preview (drag to turn it round). Pieces unlock as the bond grows; locked ones say when. "Dress for
/// the weather" puts on a raincoat in the rain and a scarf in the snow by itself. Works on a copy; Save hands it back.
/// </summary>
public sealed class OutfitWindow : Window
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x40));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x70, 0x7A, 0x8A));
    private static readonly IBrush Line = new SolidColorBrush(Color.FromRgb(0xD6, 0xDD, 0xE8));

    private readonly Pet _pet;
    private readonly Outfit _work;
    private readonly Pet _preview;
    private readonly PetPreview _view;
    private readonly DispatcherTimer _timer;
    private readonly List<(Border Chip, OutfitSlot Slot, string? Id)> _chips = new();
    private readonly Dictionary<OutfitSlot, WrapPanel> _colourRows = new();

    private readonly HashSet<Pet> _partners = new();

    public OutfitWindow(Pet pet, IReadOnlyList<Pet> others, Action<Outfit, IReadOnlyList<Pet>> onSave)
    {
        _pet = pet;
        _work = pet.Outfit.Clone();
        Title = L.S($"Dress up {pet.Name}", $"{pet.Name} 꾸미기");
        Width = 760;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = PieMenuWindow.UiFont;
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFC));

        // A stand-in with the same breed, age and colours, so the real pet only changes on Save.
        _preview = new Pet(pet.Species, pet.Name, DateTime.UtcNow, new Random(1), pet.Breed)
        {
            Growth = pet.Growth, Coat = pet.Coat, Heading = 40f, TargetHeading = 40f, Outfit = _work,
        };
        _preview.SetAnimation("sit");
        _view = new PetPreview(_preview) { Width = 270, Height = 250, Cursor = new Cursor(StandardCursorType.SizeWestEast) };
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) =>
        {
            _preview.AnimationTime += 0.05f;
            _view.InvalidateVisual();
        });
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        var turn = 40f;
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

        var rows = new StackPanel { Spacing = 12 };
        rows.Children.Add(SlotRow(OutfitSlot.Head, L.S("Head", "머리")));
        rows.Children.Add(SlotRow(OutfitSlot.Neck, L.S("Neck", "목")));
        rows.Children.Add(SlotRow(OutfitSlot.Body, L.S("Clothes", "옷")));
        rows.Children.Add(SlotRow(OutfitSlot.Face, L.S("Face", "얼굴")));

        var weather = new CheckBox
        {
            Content = L.S("Dress for the weather (raincoat in the rain, scarf and beanie in the snow)", "날씨에 맞춰 입히기 (비: 우비, 눈: 목도리·비니)"),
            IsChecked = _work.DressForWeather, FontSize = 13,
        };
        weather.IsCheckedChanged += (_, _) => _work.DressForWeather = weather.IsChecked == true;

        var bare = new Button { Content = L.S("Take everything off", "모두 벗기기"), Padding = new Thickness(14, 8), CornerRadius = new CornerRadius(10) };
        bare.Click += (_, _) => { foreach (var slot in Enum.GetValues<OutfitSlot>()) _work.Set(slot, null); Refresh(); };
        var cancel = new Button { Content = L.S("Cancel", "취소"), Padding = new Thickness(14, 8), CornerRadius = new CornerRadius(10) };
        cancel.Click += (_, _) => Close();
        var save = new Button
        {
            Content = L.S("Save", "저장"), Background = PieMenuWindow.SimsBlue, Foreground = Brushes.White, FontWeight = FontWeight.Bold,
            Padding = new Thickness(18, 8), CornerRadius = new CornerRadius(10),
        };
        save.Click += (_, _) => { onSave(_work, _partners.ToList()); Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { bare, cancel, save } };

        var love = OutfitCatalog.FashionLove(pet);
        var hint = new TextBlock
        {
            Text = (love >= 0.7f ? L.S($"{pet.Name} loves dressing up!", $"{pet.Name}은(는) 꾸미는 걸 정말 좋아해요!")
                   : love <= 0.3f ? L.S($"{pet.Name} would rather not… but it might put up with it.", $"{pet.Name}은(는) 꾸미는 걸 별로 안 좋아해요… 그래도 참아 줄지도요.")
                   : L.S($"{pet.Name} doesn't mind dressing up.", $"{pet.Name}은(는) 꾸미는 게 싫지 않아요."))
                   + "\n" + L.S("More pieces unlock as you grow closer. Drag the preview to turn it round.", "친해질수록 더 많은 아이템이 열려요. 미리보기를 끌면 돌아가요."),
            Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Width = 270,
        };
        var left = new StackPanel { Spacing = 8, Children = { _view, hint } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(18) };
        grid.Children.Add(left);
        // Couple look: dress other pets the same way (pieces they have unlocked), and they grow closer.
        var couple = new StackPanel { Spacing = 4 };
        if (others.Count > 0)
        {
            couple.Children.Add(new TextBlock
            {
                Text = L.S("Couple look: dress them the same (matching pets grow closer and pose together)", "커플룩: 똑같이 입히기 (같이 입은 펫끼리 더 친해지고 함께 포즈를 잡아요)"),
                FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Ink, TextWrapping = TextWrapping.Wrap,
            });
            var list = new WrapPanel();
            foreach (var other in others)
            {
                var chip = new Border
                {
                    Padding = new Thickness(10, 5), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(2), BorderBrush = Line, Background = Brushes.White, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = (other.Species == PetSpecies.Cat ? "🐱 " : "🐶 ") + other.Name, FontSize = 13, Foreground = Ink },
                };
                chip.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    var on = !_partners.Remove(other);
                    if (on) _partners.Add(other);
                    chip.BorderBrush = on ? PieMenuWindow.SimsBlue : Line;
                    chip.Background = on ? new SolidColorBrush(Color.FromRgb(0xE4, 0xEE, 0xFC)) : Brushes.White;
                };
                list.Children.Add(chip);
            }
            couple.Children.Add(list);
        }
        var right = new StackPanel { Spacing = 14, Margin = new Thickness(18, 0, 0, 0), Children = { rows, weather, couple, buttons } };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;
        Refresh();
    }

    private Control SlotRow(OutfitSlot slot, string label)
    {
        var items = new WrapPanel();
        items.Children.Add(Chip(slot, null, L.S("None", "없음"), locked: null));
        foreach (var item in OutfitCatalog.Of(slot))
        {
            var unlocked = OutfitCatalog.IsUnlocked(_pet, item);
            items.Children.Add(Chip(slot, item.Id, item.Name.Text, unlocked ? null : item.Bond));
        }
        var colours = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        _colourRows[slot] = colours;
        return new StackPanel
        {
            Spacing = 4,
            Children = { new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Ink }, items, colours },
        };
    }

    private Border Chip(OutfitSlot slot, string? id, string text, float? locked)
    {
        var chip = new Border
        {
            Padding = new Thickness(10, 5), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(2),
            Cursor = new Cursor(locked is null ? StandardCursorType.Hand : StandardCursorType.Arrow),
            Opacity = locked is null ? 1 : 0.5,
            Child = new TextBlock { Text = locked is null ? text : "🔒 " + text, FontSize = 13, Foreground = Ink },
        };
        if (locked is { } bond)
            ToolTip.SetTip(chip, L.S($"Unlocks at bond {bond:0}", $"유대감 {bond:0}이 되면 열려요"));
        chip.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (locked is not null) return;
            if (id is null) _work.Set(slot, null);
            else if (_work.Get(slot)?.ItemId != id) _work.Set(slot, new OutfitPiece(id, OutfitCatalog.Find(id)!.Colors[0]));
            Refresh();
        };
        _chips.Add((chip, slot, id));
        return chip;
    }

    private void Refresh()
    {
        foreach (var (chip, slot, id) in _chips)
        {
            var on = _work.Get(slot)?.ItemId == id;
            chip.BorderBrush = on ? PieMenuWindow.SimsBlue : Line;
            chip.Background = on ? new SolidColorBrush(Color.FromRgb(0xE4, 0xEE, 0xFC)) : Brushes.White;
        }
        // Colours of the piece worn in each slot.
        foreach (var (slot, row) in _colourRows)
        {
            row.Children.Clear();
            if (_work.Get(slot) is not { } piece || piece.Item is not { } item) continue;
            foreach (var rgb in item.Colors)
            {
                var sw = new Border
                {
                    Width = 24, Height = 24, Margin = new Thickness(0, 0, 6, 4), CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(3), Background = new SolidColorBrush(BreedLook.C(rgb)),
                    BorderBrush = piece.Color == rgb ? PieMenuWindow.SimsBlue : Line, Cursor = new Cursor(StandardCursorType.Hand),
                };
                var c = rgb;
                sw.PointerPressed += (_, e) => { _work.Set(slot, piece with { Color = c }); Refresh(); e.Handled = true; };
                row.Children.Add(sw);
            }
        }
        _view.InvalidateVisual();
    }
}
