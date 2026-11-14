using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PetFam.Core;
using PetFam.Desktop.Ai;

namespace PetFam.Desktop.Windows;

/// <summary>
/// Which AI the pets use (whatever the user already has: Claude Code, Codex, Gemini CLI, Ollama, or an API key),
/// what it is used for, a test button, and connecting Claude Code to PetFam (MCP tools + agent buddy hooks).
/// </summary>
public sealed class AiSettingsWindow : Window
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1C, 0x2A, 0x40));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x70, 0x7A, 0x8A));

    public AiSettingsWindow(AiHub hub, bool talkOn, Action<bool> setTalk, Func<Task<string>> connectClaudeCode, string serverInfo)
    {
        Title = L.S("AI for your pets", "펫을 위한 AI");
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = PieMenuWindow.UiFont;
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFC));

        var s = hub.Settings;
        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        stack.Children.Add(Text(L.S("Pets can use an AI you already have to talk in their own voice, chat with you, keep a diary and make things from your words. Only pet information goes to the AI, never what is on your screen.",
            "이미 쓰고 있는 AI로 펫이 자기 말투로 대화하고, 나와 이야기하고, 일기를 쓰고, 말로 꾸밀 수 있어요. AI에는 펫 정보만 보내고 화면 내용은 절대 보내지 않아요."), Muted, 12));

        stack.Children.Add(Header(L.S("Which AI", "어떤 AI를 쓸까요")));
        var group = "ai-provider";
        RadioButton Radio(AiProvider p, string label, bool found, string? note = null)
        {
            var rb = new RadioButton
            {
                GroupName = group, IsChecked = s.Provider == p, IsEnabled = found || p is AiProvider.Auto or AiProvider.Off or AiProvider.AnthropicApi,
                Content = Text(label + (note is null ? "" : "  · " + note), found ? Ink : Muted, 13),
            };
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true) s.Provider = p; };
            return rb;
        }
        string Found(bool f) => f ? L.S("found", "설치됨") : L.S("not installed", "설치 안 됨");
        stack.Children.Add(Radio(AiProvider.Auto, L.S("Automatic (the first one found)", "자동 (찾은 것 중 첫 번째)"), true,
            hub.Writer is null ? L.S("none yet", "아직 없음") : L.S($"now: {hub.Writer.Name}", $"지금: {hub.Writer.Name}")));
        stack.Children.Add(Radio(AiProvider.ClaudeCli, L.S("Claude Code (your Claude subscription)", "Claude Code (내 Claude 구독)"), hub.Claude is not null, Found(hub.Claude is not null)));
        stack.Children.Add(Radio(AiProvider.CodexCli, L.S("Codex CLI (your ChatGPT subscription)", "Codex CLI (내 ChatGPT 구독)"), hub.Codex is not null, Found(hub.Codex is not null)));
        stack.Children.Add(Radio(AiProvider.GeminiCli, L.S("Gemini CLI (your Google account)", "Gemini CLI (내 구글 계정)"), hub.Gemini is not null, Found(hub.Gemini is not null)));

        var ollamaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        ollamaRow.Children.Add(Radio(AiProvider.Ollama, L.S("Ollama (a model on this computer)", "Ollama (내 컴퓨터의 모델)"), hub.OllamaModels is not null,
            hub.OllamaModels is null ? L.S("not running", "실행 안 됨") : null));
        if (hub.OllamaModels is { } models)
        {
            var pick = new ComboBox { ItemsSource = models, SelectedItem = s.OllamaModel is { } m && models.Contains(m) ? m : models[0], MinWidth = 180 };
            pick.SelectionChanged += (_, _) => s.OllamaModel = pick.SelectedItem as string;
            ollamaRow.Children.Add(pick);
        }
        stack.Children.Add(ollamaRow);

        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        keyRow.Children.Add(Radio(AiProvider.AnthropicApi, L.S("Claude API key", "Claude API 키"), true, L.S("billed to the key", "키로 따로 과금")));
        var key = new TextBox { PasswordChar = '•', Text = s.AnthropicKey ?? "", Width = 260, Watermark = "sk-ant-…" };
        key.TextChanged += (_, _) => s.AnthropicKey = string.IsNullOrWhiteSpace(key.Text) ? null : key.Text.Trim();
        keyRow.Children.Add(key);
        stack.Children.Add(keyRow);
        stack.Children.Add(Radio(AiProvider.Off, L.S("Off (no AI)", "끄기 (AI 안 씀)"), true));

        stack.Children.Add(Header(L.S("What it is for", "어디에 쓸까요")));
        var talk = Check(L.S("Pets chat with each other in their own personas", "펫끼리 각자의 성격대로 대화하기"), talkOn);
        var diary = Check(L.S("Pets write a diary every night (and a weekly story)", "펫이 밤마다 일기 쓰기 (주간 이야기도)"), s.Diary);
        diary.IsCheckedChanged += (_, _) => s.Diary = diary.IsChecked == true;
        var habitat = Check(L.S("Read each new wallpaper and work out the habitat (needs an AI that can see pictures)", "새 바탕화면을 AI가 보고 서식지 자동 판단 (그림을 볼 수 있는 AI 필요)"), s.AutoHabitat);
        habitat.IsCheckedChanged += (_, _) => s.AutoHabitat = habitat.IsChecked == true;
        stack.Children.Add(talk);
        stack.Children.Add(diary);
        stack.Children.Add(habitat);
        stack.Children.Add(Text(L.S("Talking to a pet (click it → Talk) and making outfits, houses and pets from words use it whenever you ask.",
            "펫에게 말 걸기(펫 클릭 → 말 걸기)와 말로 옷·집·펫 만들기는 요청할 때마다 써요."), Muted, 12));

        stack.Children.Add(Header(L.S("Connect your AI apps", "AI 앱과 연결하기")));
        var server = Check(L.S("Local server for AI apps (MCP tools) and coding agents (127.0.0.1 only)", "AI 앱(MCP 도구)과 코딩 에이전트용 로컬 서버 (127.0.0.1 전용)"), s.LocalServer);
        server.IsCheckedChanged += (_, _) => s.LocalServer = server.IsChecked == true;
        var buddy = Check(L.S("Pets cheer on your coding agent (working, done, needs you)", "코딩 에이전트를 펫이 응원하기 (작업 중, 완료, 집사 호출)"), s.AgentBuddy);
        buddy.IsCheckedChanged += (_, _) => s.AgentBuddy = buddy.IsChecked == true;
        stack.Children.Add(server);
        stack.Children.Add(buddy);
        var info = new SelectableTextBlock { Text = serverInfo, FontFamily = new FontFamily("Consolas, Malgun Gothic"), FontSize = 11, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0xDD, 0xE8)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Child = info });
        var connectResult = Text("", Muted, 12);
        var connect = new Button { Content = L.S("Connect Claude Code (adds the MCP server and hooks)", "Claude Code에 연결 (MCP 서버와 훅 추가)"), IsEnabled = hub.Claude is not null, Padding = new Thickness(12, 6) };
        connect.Click += async (_, _) =>
        {
            connect.IsEnabled = false;
            connectResult.Text = L.S("Connecting…", "연결하는 중…");
            connectResult.Text = await connectClaudeCode();
            connect.IsEnabled = true;
        };
        stack.Children.Add(connect);
        stack.Children.Add(connectResult);

        var testResult = Text("", Muted, 12);
        var test = new Button { Content = L.S("Test", "시험해 보기"), Padding = new Thickness(14, 8), CornerRadius = new CornerRadius(10) };
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            hub.Apply(s);
            testResult.Text = L.S($"Asking {hub.Writer?.Name ?? "nobody"}…", $"{hub.Writer?.Name ?? "없음"}에게 물어보는 중…");
            var started = DateTime.UtcNow;
            var answer = await hub.AskAsync("You are a cat in a desktop pet game.", L.Ko ? "집사에게 한 문장으로 인사해." : "Greet your owner in one sentence.", TimeSpan.FromSeconds(60));
            testResult.Text = answer is null
                ? L.S("No answer (see the log).", "대답이 없어요 (로그를 확인해 주세요).")
                : $"{answer.Trim()}  ({(DateTime.UtcNow - started).TotalSeconds:0.0}s)";
            test.IsEnabled = true;
        };
        var close = new Button { Content = L.S("Save and close", "저장하고 닫기"), Background = PieMenuWindow.SimsBlue, Foreground = Brushes.White, FontWeight = FontWeight.Bold, Padding = new Thickness(18, 8), CornerRadius = new CornerRadius(10) };
        close.Click += (_, _) =>
        {
            hub.Apply(s);
            setTalk(talk.IsChecked == true);
            Close();
        };
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { test, close } });
        stack.Children.Add(testResult);

        Content = new ScrollViewer { Content = stack, MaxHeight = 860 };
    }

    private static TextBlock Text(string text, IBrush brush, double size) =>
        new() { Text = text, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Header(string text) =>
        new() { Text = text, Foreground = Ink, FontSize = 14, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 8, 0, 0) };

    private static CheckBox Check(string text, bool on) => new() { Content = Text(text, Ink, 13), IsChecked = on };
}
