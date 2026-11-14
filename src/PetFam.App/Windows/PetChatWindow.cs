using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PetFam.Core;
using PetFam.Core.Pets;

namespace PetFam.Desktop.Windows;

/// <summary>
/// Talk to a pet: type something, it answers in its own voice (and in a speech bubble on the desktop), and it may
/// do what you asked if it is willing ("Butter, go have a rest").
/// </summary>
public sealed class PetChatWindow : Window
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x40));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x70, 0x7A, 0x8A));

    private readonly Pet _pet;
    private readonly Func<string, Task<string?>> _send;
    private readonly StackPanel _lines = new() { Spacing = 6 };
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input;
    private readonly Button _sendButton;

    public PetChatWindow(Pet pet, IReadOnlyList<string> history, Func<string, Task<string?>> send)
    {
        _pet = pet;
        _send = send;
        Title = L.S($"Talk to {pet.Name}", $"{pet.Name}에게 말 걸기");
        Width = 420;
        Height = 480;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = PieMenuWindow.UiFont;
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFC));

        foreach (var line in history.TakeLast(12))
        {
            var mine = line.StartsWith("Owner: ");
            Add(mine ? line["Owner: ".Length..] : line[(line.IndexOf(": ", StringComparison.Ordinal) + 2)..], mine);
        }
        if (history.Count == 0)
            _lines.Children.Add(new TextBlock
            {
                Text = L.S($"Say hi to {pet.Name}. You can ask it to do things too (\"go have a rest\").", $"{pet.Name}에게 인사해 보세요. 부탁도 할 수 있어요 (\"가서 좀 쉬어\")."),
                Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            });

        _scroll = new ScrollViewer { Content = _lines };
        _input = new TextBox { Watermark = L.S("Say something…", "하고 싶은 말…"), MaxLength = 200 };
        _sendButton = new Button { Content = L.S("Send", "보내기"), Background = PieMenuWindow.SimsBlue, Foreground = Brushes.White, Padding = new Thickness(14, 6) };
        _sendButton.Click += async (_, _) => await Send();
        _input.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await Send();
        };

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(_sendButton, Dock.Right);
        bottom.Children.Add(_sendButton);
        _sendButton.Margin = new Thickness(8, 0, 0, 0);
        bottom.Children.Add(_input);

        var dock = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);
        dock.Children.Add(_scroll);
        Content = dock;
        Opened += (_, _) => _input.Focus();
    }

    private async Task Send()
    {
        var text = _input.Text?.Trim();
        if (string.IsNullOrEmpty(text) || !_sendButton.IsEnabled) return;
        _input.Text = "";
        Add(text, mine: true);
        _sendButton.IsEnabled = false;
        var thinking = new TextBlock { Text = L.S($"{_pet.Name} is thinking…", $"{_pet.Name}이(가) 생각하는 중…"), Foreground = Muted, FontSize = 12 };
        _lines.Children.Add(thinking);
        _scroll.ScrollToEnd();
        var reply = await _send(text);
        _lines.Children.Remove(thinking);
        Add(reply ?? L.S($"({_pet.Name} just stares at you. The AI didn't answer.)", $"({_pet.Name}이(가) 멀뚱히 쳐다봐요. AI가 대답하지 않았어요.)"), mine: false);
        _sendButton.IsEnabled = true;
        _input.Focus();
    }

    private void Add(string text, bool mine)
    {
        _lines.Children.Add(new Border
        {
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 300,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 6),
            Background = mine ? new SolidColorBrush(Color.FromRgb(0xDC, 0xE8, 0xFB)) : Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0xDD, 0xE8)),
            BorderThickness = new Thickness(1),
            Child = new SelectableTextBlock
            {
                Text = mine ? text : $"{_pet.Name}: {text}", FontSize = 13, Foreground = Ink, TextWrapping = TextWrapping.Wrap,
            },
        });
        _scroll?.ScrollToEnd();
    }
}
