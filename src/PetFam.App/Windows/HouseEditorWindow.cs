using PetFam.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PetFam.Core.World;
using PetFam.Desktop.Rendering;

namespace PetFam.Desktop.Windows;

/// <summary>
/// House editor: name the house, pick its shape, roof and wall colours and decorations, with a live preview.
/// Works on a copy; Build / Save hands the copy back, Demolish tears an existing house down.
/// </summary>
public sealed class HouseEditorWindow : Window
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x40));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x70, 0x7A, 0x8A));
    private static readonly IBrush Selected = PieMenuWindow.SimsBlue;

    private readonly PetHouse _work;
    private readonly HousePreview _preview;
    private readonly DispatcherTimer _timer;
    private readonly List<(Border Chip, Func<bool> IsOn)> _chips = new();

    /// <param name="house">The house to edit (a copy is edited), or a fresh one to build.</param>
    /// <param name="isNew">Shows Build instead of Save, and no Demolish.</param>
    /// <param name="pets">Who the house can belong to (id, name, species).</param>
    public HouseEditorWindow(PetHouse house, bool isNew, Action<PetHouse> onSave, Action? onDemolish,
        IReadOnlyList<(Guid Id, string Name, PetFam.Core.Pets.PetSpecies Species)>? pets = null)
    {
        _work = house.Clone();
        Title = isNew ? L.S("Build a new house", "새 집 짓기") : L.S("Decorate the house", "집 꾸미기");
        Width = 640;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = PieMenuWindow.UiFont;
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFC));

        _preview = new HousePreview { House = _work, Width = 260, Height = 230 };
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) => _preview.InvalidateVisual());
        _timer.Start();
        Closed += (_, _) => _timer.Stop();

        var name = new TextBox { Text = _work.Name, MaxLength = 12, Watermark = L.S("House name (up to 12 letters)", "집 이름 (8자까지)"), FontSize = 14 };
        name.TextChanged += (_, _) => { _work.Name = name.Text ?? ""; _preview.InvalidateVisual(); };

        var options = new StackPanel { Spacing = 10 };

        // Whose house: that pet goes in (to sleep, out of the rain); other pets leave it alone. Everybody's house: anyone.
        options.Children.Add(Label(L.S("Whose house is it?", "누구 집인가요?")));
        var owners = new WrapPanel();
        string AutoName(Guid? owner) => owner is { } id && pets?.FirstOrDefault(p => p.Id == id) is { Name.Length: > 0 } p ? AppHost.HouseNameFor(p.Name) : L.S("Our house", "우리 집");
        void SetOwner(Guid? owner)
        {
            // Keep a name the user typed; follow the owner while it is still an automatic one.
            var auto = _work.Name.Length == 0 || _work.Name.StartsWith("Our house") || _work.Name.StartsWith("우리 집")
                       || pets?.Any(p => _work.Name == $"{p.Name}네" || _work.Name == $"{p.Name}'s house") == true;
            _work.OwnerId = owner;
            if (auto) { _work.Name = AutoName(owner); name.Text = _work.Name; }
            _preview.InvalidateVisual();
        }
        owners.Children.Add(Chip(L.S("Everybody's house", "모두의 집"), () => _work.OwnerId is null, () => SetOwner(null)));
        foreach (var p in pets ?? Array.Empty<(Guid, string, PetFam.Core.Pets.PetSpecies)>())
        {
            var label = (p.Species == PetFam.Core.Pets.PetSpecies.Cat ? "🐱 " : "🐶 ") + p.Name;
            owners.Children.Add(Chip(label, () => _work.OwnerId == p.Id, () => SetOwner(p.Id)));
        }
        options.Children.Add(owners);

        options.Children.Add(Label(L.S("Name", "이름")));
        options.Children.Add(name);

        options.Children.Add(Label(L.S("Shape", "모양")));
        var shapes = new WrapPanel();
        foreach (var (shape, label) in HouseArt.Shapes)
            shapes.Children.Add(Chip(label, () => _work.Shape == shape, () => _work.Shape = shape));
        options.Children.Add(shapes);

        options.Children.Add(Label(L.S("Roof colour", "지붕 색")));
        var roofs = new WrapPanel();
        for (var i = 0; i < HouseArt.RoofColors.Length; i++)
        {
            var k = i;
            roofs.Children.Add(Swatch(HouseArt.RoofColors[k], () => _work.RoofColor == k, () => _work.RoofColor = k));
        }
        options.Children.Add(roofs);

        options.Children.Add(Label(L.S("Wall colour", "벽 색")));
        var walls = new WrapPanel();
        for (var i = 0; i < HouseArt.WallColors.Length; i++)
        {
            var k = i;
            walls.Children.Add(Swatch(HouseArt.WallColors[k], () => _work.WallColor == k, () => _work.WallColor = k));
        }
        options.Children.Add(walls);

        options.Children.Add(Label(L.S("Decorations (pick as many as you like)", "장식 (여러 개 고를 수 있어요)")));
        var decos = new WrapPanel();
        foreach (var (id, label) in HouseArt.Decorations)
            decos.Children.Add(Chip(label, () => _work.Decorations.Contains(id), () =>
            {
                if (!_work.Decorations.Remove(id)) _work.Decorations.Add(id);
            }));
        options.Children.Add(decos);

        var save = new Button
        {
            Content = isNew ? L.S("Build", "집 짓기") : L.S("Save", "저장"),
            Background = Selected,
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            Padding = new Thickness(18, 8),
            CornerRadius = new CornerRadius(10),
        };
        save.Click += (_, _) => { onSave(_work); Close(); };
        var cancel = new Button { Content = L.S("Cancel", "취소"), Padding = new Thickness(14, 8), CornerRadius = new CornerRadius(10) };
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (!isNew && onDemolish is not null)
        {
            var demolish = new Button
            {
                Content = L.S("Demolish", "집 허물기"),
                Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x3A, 0x30)),
                Padding = new Thickness(14, 8),
                CornerRadius = new CornerRadius(10),
            };
            var armed = false;
            demolish.Click += (_, _) =>
            {
                if (!armed)
                {
                    armed = true;
                    demolish.Content = L.S("Really demolish? Click again", "정말 허물까요? 한 번 더");
                    DispatcherTimer.RunOnce(() => { armed = false; demolish.Content = L.S("Demolish", "집 허물기"); }, TimeSpan.FromSeconds(3));
                    return;
                }
                onDemolish();
                Close();
            };
            buttons.Children.Add(demolish);
        }
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var hint = new TextBlock
        {
            Text = isNew ? L.S("Once built it goes on the floor. Drag it to move it, click it to decorate again.\nIts owner sleeps in it and shelters from the rain. Anyone may use everybody's house.", "다 지으면 바닥에 놓여요. 집을 끌어서 옮기고, 눌러서 다시 꾸밀 수 있어요.\n주인은 졸릴 때 들어가 자고, 비가 오면 비를 피해요. 모두의 집에는 누구나 들어가요.")
                         : L.S("Drag the house to move it. Its owner sleeps in it and shelters from the rain.", "집을 끌어서 옮길 수 있어요. 주인은 졸릴 때 들어가 자고, 비가 오면 비를 피해요."),
            Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        };

        var left = new StackPanel { Spacing = 8, Children = { _preview, hint }, Width = 260 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(18) };
        grid.Children.Add(left);
        var right = new StackPanel { Spacing = 12, Margin = new Thickness(18, 0, 0, 0), Children = { options, buttons } };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;
        Refresh();
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Ink };

    private Border Chip(string text, Func<bool> isOn, Action toggle)
    {
        var chip = new Border
        {
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(2),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = text, FontSize = 13 },
        };
        chip.PointerPressed += (_, e) => { toggle(); Refresh(); e.Handled = true; };
        _chips.Add((chip, isOn));
        return chip;
    }

    private Border Swatch((Loc Name, Color Color) c, Func<bool> isOn, Action select)
    {
        var sw = new Border
        {
            Width = 30, Height = 30,
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(15),
            Background = new SolidColorBrush(c.Color),
            BorderThickness = new Thickness(3),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(sw, c.Name);
        sw.PointerPressed += (_, e) => { select(); Refresh(); e.Handled = true; };
        _chips.Add((sw, isOn));
        return sw;
    }

    private void Refresh()
    {
        foreach (var (chip, isOn) in _chips)
        {
            var on = isOn();
            chip.BorderBrush = on ? Selected : new SolidColorBrush(Color.FromRgb(0xD6, 0xDD, 0xE8));
            if (chip.Child is TextBlock tb)
            {
                chip.Background = on ? new SolidColorBrush(Color.FromRgb(0xE4, 0xEE, 0xFC)) : Brushes.White;
                tb.Foreground = on ? Selected : Ink;
            }
        }
        _preview.InvalidateVisual();
    }
}
