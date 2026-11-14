using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PetFam.Core;
using PetFam.Core.Pets;
using PetFam.Core.Talk;

namespace PetFam.Desktop.Windows;

/// <summary>
/// The pets' diaries (one entry a night, in each pet's own voice) and the weekly stories about all of them.
/// "Write today's now" and "Write this week's story" ask the AI straight away.
/// </summary>
public sealed class DiaryWindow : Window
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x40));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x70, 0x7A, 0x8A));
    private static readonly IBrush Paper = new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xF0));

    private readonly IReadOnlyList<Pet> _pets;
    private readonly Journal _journal;
    private readonly StackPanel _entries = new() { Spacing = 10 };
    private readonly ComboBox _who = new() { MinWidth = 200 };
    private readonly TextBlock _status = new() { Foreground = Muted, FontSize = 12 };

    public DiaryWindow(IReadOnlyList<Pet> pets, Journal journal, Pet? first, bool aiOn,
        Func<Pet, Task<string?>> writeToday, Func<Task<StoryEntry?>> writeStory)
    {
        _pets = pets;
        _journal = journal;
        Title = L.S("Pet diaries", "펫 일기장");
        Width = 560;
        Height = 640;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = PieMenuWindow.UiFont;
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFC));

        var choices = pets.Select(p => (object)p.Name).Append(L.S("📖 Weekly stories", "📖 주간 이야기")).ToList();
        _who.ItemsSource = choices;
        _who.SelectedIndex = first is null ? 0 : Math.Max(0, pets.ToList().IndexOf(first));
        _who.SelectionChanged += (_, _) => Fill();

        var today = new Button { Content = L.S("Write today's entry now", "오늘 일기 지금 쓰기"), IsEnabled = aiOn, Padding = new Thickness(12, 6) };
        today.Click += async (_, _) =>
        {
            if (Selected is not { } pet) return;
            today.IsEnabled = false;
            _status.Text = L.S($"{pet.Name} is writing…", $"{pet.Name}이(가) 쓰는 중…");
            var text = await writeToday(pet);
            _status.Text = text is null ? L.S("No answer from the AI.", "AI가 대답하지 않았어요.") : "";
            today.IsEnabled = true;
            Fill();
        };
        var story = new Button { Content = L.S("Write this week's story", "이번 주 이야기 쓰기"), IsEnabled = aiOn, Padding = new Thickness(12, 6) };
        story.Click += async (_, _) =>
        {
            story.IsEnabled = false;
            _status.Text = L.S("Writing the story…", "이야기를 쓰는 중…");
            var s = await writeStory();
            _status.Text = s is null ? L.S("No story (it needs some diary entries this week).", "이야기를 못 썼어요 (이번 주 일기가 있어야 해요).") : "";
            story.IsEnabled = true;
            _who.SelectedIndex = choices.Count - 1;
            Fill();
        };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _who, today, story } };
        var hint = new TextBlock
        {
            Text = aiOn
                ? L.S("Each pet writes about its day every night after 9 pm, in its own voice. A story about all of them comes once a week.",
                      "펫들은 밤 9시가 지나면 그날 있었던 일을 자기 말투로 써요. 일주일에 한 번은 모두의 이야기가 나와요.")
                : L.S("Set up an AI in the tray (AI for your pets…) and the pets will start keeping diaries.",
                      "트레이의 'AI 설정'에서 AI를 연결하면 펫들이 일기를 쓰기 시작해요."),
            Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        };
        var dock = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 10), Children = { top, hint, _status } };
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(new ScrollViewer { Content = _entries });
        Content = dock;
        Fill();
    }

    private Pet? Selected => _who.SelectedIndex >= 0 && _who.SelectedIndex < _pets.Count ? _pets[_who.SelectedIndex] : null;

    private void Fill()
    {
        _entries.Children.Clear();
        if (Selected is { } pet)
        {
            var list = _journal.DiariesOf(pet).ToList();
            if (list.Count == 0) _entries.Children.Add(Note(L.S($"{pet.Name} hasn't written anything yet.", $"{pet.Name}은(는) 아직 일기를 안 썼어요.")));
            foreach (var d in list) _entries.Children.Add(Card(d.Date.ToString(L.Ko ? "yyyy년 M월 d일 (ddd)" : "ddd, MMM d yyyy"), d.Text));
        }
        else
        {
            var stories = _journal.Stories.OrderByDescending(s => s.Date).ToList();
            if (stories.Count == 0) _entries.Children.Add(Note(L.S("No stories yet.", "아직 이야기가 없어요.")));
            foreach (var s in stories) _entries.Children.Add(Card($"{s.Title} · {s.Date:yyyy-MM-dd}", s.Text));
        }
    }

    private static TextBlock Note(string text) => new() { Text = text, Foreground = Muted, FontSize = 13 };

    private static Border Card(string title, string text) => new()
    {
        Background = Paper, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xEA, 0xDF, 0xC4)), BorderThickness = new Thickness(1),
        Child = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 13, Foreground = Ink },
                new SelectableTextBlock { Text = text, FontSize = 14, Foreground = Ink, TextWrapping = TextWrapping.Wrap, LineHeight = 22 },
            },
        },
    };
}
