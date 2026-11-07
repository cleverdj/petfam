using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ZooZoo.Core.Interactions;
using ZooZoo.Core.Pets;
using ZooZoo.Core.Platform;

namespace ZooZoo.Desktop.Windows;

/// <summary>
/// Sims-style info panel for the selected pet: mood, the five needs ("motives"), bond, and the action queue.
/// Clicking a queued action cancels it. Sits in the bottom-right corner above the taskbar.
/// </summary>
public sealed class PetPanelWindow : Window
{
    private const double PanelWidth = 280;

    private readonly IPlatform _platform;
    private readonly IWindowLayering _layering;
    private readonly Action<Pet, QueuedAction> _onCancel;
    private readonly Action _onClose;
    private readonly PixelRect _workArea;

    private readonly TextBlock _name = new() { FontFamily = PieMenuWindow.UiFont, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Ink };
    private readonly TextBlock _species = new() { FontFamily = PieMenuWindow.UiFont, FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 2) };
    private readonly Border _moodDot = new() { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _moodText = new() { FontFamily = PieMenuWindow.UiFont, FontSize = 13, Foreground = Ink, Margin = new Thickness(6, 0, 0, 0) };
    private readonly (string Label, Border Fill, TextBlock Value)[] _bars;
    private readonly Border _bondFill = new() { Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _bondText = new() { FontFamily = PieMenuWindow.UiFont, FontSize = 12, Foreground = Ink };
    private readonly WrapPanel _queue = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _social = new() { FontFamily = PieMenuWindow.UiFont, FontSize = 12, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
    public Func<Pet, string>? SocialLine { get; set; }
    private string _queueKey = "";

    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x40));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x70, 0x7A, 0x8A));
    private const double BarWidth = 134;

    public Pet? Pet { get; private set; }

    public PetPanelWindow(IPlatform platform, IWindowLayering layering, PixelRect workArea, Action<Pet, QueuedAction> onCancel, Action onClose)
    {
        _platform = platform;
        _layering = layering;
        _workArea = workArea;
        _onCancel = onCancel;
        _onClose = onClose;

        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Height;
        Width = PanelWidth + 20;

        string[] labels = { "배고픔", "에너지", "재미", "애정", "청결" };
        _bars = labels.Select(l => (l, new Border { Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left },
            new TextBlock { FontFamily = PieMenuWindow.UiFont, FontSize = 11, Foreground = Muted, Width = 28, TextAlignment = TextAlignment.Right })).ToArray();

        var close = new TextBlock { Text = "✕", FontSize = 14, Foreground = Muted, Cursor = new Cursor(StandardCursorType.Hand), HorizontalAlignment = HorizontalAlignment.Right };
        close.PointerPressed += (_, e) => { e.Handled = true; _onClose(); };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { _name, _species } };
        header.Children.Add(nameRow);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(header);
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _moodDot, _moodText } });
        stack.Children.Add(Separator());
        foreach (var (label, fill, value) in _bars)
            stack.Children.Add(BarRow(label, fill, value, null));
        stack.Children.Add(Separator());
        stack.Children.Add(BarRow("유대감", _bondFill, null, _bondText));
        stack.Children.Add(_social);
        stack.Children.Add(Separator());
        stack.Children.Add(new TextBlock { Text = "할 일 (눌러서 취소)", FontFamily = PieMenuWindow.UiFont, FontSize = 12, Foreground = Muted });
        stack.Children.Add(_queue);

        Content = new Border
        {
            Margin = new Thickness(10),
            Width = PanelWidth,
            Padding = new Thickness(14, 12),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(0xF6, 0xFA, 0xFB, 0xFD)),
            BorderBrush = PieMenuWindow.SimsBlue,
            BorderThickness = new Thickness(2),
            BoxShadow = BoxShadows.Parse("0 4 14 0 #55000000"),
            Child = stack,
        };

        Opened += (_, _) =>
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;
            _platform.RegisterOwnWindow(handle);
            try { _layering.MakeFrontWindow(handle); }
            catch (Exception ex) { AppLog.Write("Panel window style failed: " + ex.Message); }
        };
    }

    public void ShowFor(Pet pet)
    {
        Pet = pet;
        _name.Text = pet.Name;
        _species.Text = pet.Species.DisplayName();
        _queueKey = "";
        Refresh();
        if (!IsVisible) Show();
        AppLog.Write($"Panel shown for {pet.Name}: pos={Position} bounds={Bounds} visible={IsVisible} scaling={RenderScaling}");
    }

    /// <summary>Re-reads the pet's state into the bars and the queue chips. Cheap; call a few times per second.</summary>
    public void Refresh()
    {
        if (Pet is not { } pet) return;

        var level = MoodCalc.Level(pet);
        _moodDot.Background = MoodBrush(level);
        _moodText.Text = MoodCalc.Label(level);

        float[] values = { pet.Needs.Hunger, pet.Needs.Energy, pet.Needs.Fun, pet.Needs.Affection, MoodCalc.Hygiene(pet) };
        for (var i = 0; i < _bars.Length; i++)
        {
            var v = Math.Clamp(values[i], 0f, 100f);
            _bars[i].Fill.Width = Math.Max(4, BarWidth * v / 100);
            _bars[i].Fill.Background = v >= 60 ? Good : v >= 30 ? Okay : Bad;
            _bars[i].Value.Text = ((int)v).ToString();
        }

        _bondFill.Width = Math.Max(4, BarWidth * pet.Bond.Value / 100);
        _bondFill.Background = new SolidColorBrush(Color.FromRgb(0x9B, 0x6B, 0xDF));
        _bondText.Text = $"{BondNames.Of(pet.Bond.Stage)} {pet.Bond.Value:F0}";
        _social.Text = SocialLine?.Invoke(pet) ?? "";
        _social.IsVisible = _social.Text.Length > 0;

        RefreshQueue(pet);
        PlaceInCorner();
    }

    private void RefreshQueue(Pet pet)
    {
        var key = string.Join(",", pet.Queue.Select(q => $"{q.Id}:{q.Status}"));
        if (key == _queueKey)
        {
            // Only the running chip's progress changes.
            if (pet.Queue.Count > 0 && _queue.Children.Count > 0 && _queue.Children[0] is Border { Tag: ProgressBar bar })
                bar.Value = pet.Queue[0].Progress * 100;
            return;
        }

        _queueKey = key;
        _queue.Children.Clear();
        if (pet.Queue.Count == 0)
        {
            _queue.Children.Add(new TextBlock { Text = "비어 있음", FontFamily = PieMenuWindow.UiFont, FontSize = 12, Foreground = Muted });
            return;
        }

        foreach (var action in pet.Queue.ToList())
        {
            var running = action.Status == QueueStatus.Running;
            var progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = action.Progress * 100, Height = 3, MinHeight = 3, IsVisible = running, Foreground = PieMenuWindow.SimsBlue };
            var chip = new Border
            {
                Tag = progress,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(8, 4),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(running ? Color.FromRgb(0xDC, 0xEA, 0xFF) : Color.FromRgb(0xEE, 0xF1, 0xF5)),
                BorderBrush = running ? PieMenuWindow.SimsBlue : new SolidColorBrush(Color.FromRgb(0xC8, 0xCF, 0xD9)),
                BorderThickness = new Thickness(1.5),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 4,
                            Children =
                            {
                                new TextBlock { Text = action.Interaction.Icon, FontFamily = PieMenuWindow.EmojiFont, FontSize = 13 },
                                new TextBlock { Text = action.Interaction.Label, FontFamily = PieMenuWindow.UiFont, FontSize = 12, Foreground = Ink },
                            },
                        },
                        progress,
                    },
                },
            };
            ToolTip.SetTip(chip, "눌러서 취소");
            chip.PointerPressed += (_, e) => { e.Handled = true; _onCancel(pet, action); };
            _queue.Children.Add(chip);
        }
    }

    private void PlaceInCorner()
    {
        var scale = RenderScaling;
        var w = (int)((PanelWidth + 20) * scale);
        var h = (int)(Math.Max(Bounds.Height, 200) * scale);
        var target = new PixelPoint(_workArea.Right - w - 8, _workArea.Bottom - h - 8);
        if (Position != target) Position = target;
    }

    private static readonly IBrush Good = new SolidColorBrush(Color.FromRgb(0x4C, 0xC0, 0x5E));
    private static readonly IBrush Okay = new SolidColorBrush(Color.FromRgb(0xF0, 0xBE, 0x3A));
    private static readonly IBrush Bad = new SolidColorBrush(Color.FromRgb(0xE0, 0x52, 0x46));

    public static IBrush MoodBrush(MoodLevel level) => level switch
    {
        MoodLevel.Good => Good,
        MoodLevel.Okay => Okay,
        _ => Bad,
    };

    private static Control BarRow(string label, Border fill, TextBlock? value, TextBlock? trailing)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("56,Auto,*") };
        grid.Children.Add(new TextBlock { Text = label, FontFamily = PieMenuWindow.UiFont, FontSize = 12, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
        var track = new Border
        {
            Width = BarWidth,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromRgb(0xE3, 0xE7, 0xEE)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = fill,
        };
        Grid.SetColumn(track, 1);
        grid.Children.Add(track);
        var tail = (Control?)value ?? trailing;
        if (tail is not null)
        {
            tail.Margin = new Thickness(8, 0, 0, 0);
            tail.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tail, 2);
            grid.Children.Add(tail);
        }
        return grid;
    }

    private static Control Separator() => new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xE1, 0xE6, 0xEE)), Margin = new Thickness(0, 2) };
}
