using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using PetFam.Core;
using PetFam.Core.Interactions;
using PetFam.Core.Pets;
using PetFam.Core.Talk;
using PetFam.Desktop.Ai;
using PetFam.Desktop.Windows;

namespace PetFam.Desktop;

/// <summary>
/// Everything the user's own AI does for the pets: settings, the local server (MCP tools for AI apps, coding-agent
/// events), talking with a pet, diaries and weekly stories. Without an AI none of this runs and nothing changes.
/// </summary>
public sealed partial class AppHost
{
    private AiServer? _server;
    private DispatcherTimer? _aiTimer;
    private AiSettingsWindow? _aiWindow;

    /// <summary>Starts what depends on the AI settings: the local server and the nightly diary check.</summary>
    private void StartAiServices()
    {
        RestartServer();
        _aiTimer = new DispatcherTimer(TimeSpan.FromSeconds(DiaryNow ? 20 : 60), DispatcherPriority.Background, (_, _) => _ = DiaryTickAsync());
        _aiTimer.Start();
        Ai.Changed += RestartServer;
    }

    private void RestartServer()
    {
        var want = Ai.Settings.LocalServer;
        if (_server is not null && (!want || _server.Port != Ai.Settings.Port))
        {
            _server.Dispose();
            _server = null;
        }
        if (want && _server is null)
        {
            var server = new AiServer(Ai.Settings.Port, McpTools(), OnAgentEvent);
            if (server.Start()) _server = server;
        }
    }

    private string ServerInfo()
    {
        var url = $"http://127.0.0.1:{Ai.Settings.Port}";
        return L.S("MCP server: ", "MCP 서버: ") + url + "/mcp\n" +
               "Claude Code:  claude mcp add --transport http --scope user petfam " + url + "/mcp\n" +
               L.S("Claude Desktop and others: ", "Claude Desktop 등: ") + "npx mcp-remote " + url + "/mcp\n" +
               L.S("Coding agent events: ", "코딩 에이전트 이벤트: ") + "curl \"" + url + "/agent?event=done\"  (working · done · waiting · failed)";
    }

    /// <summary>Tray: which AI, what for, test, connect Claude Code.</summary>
    public void OpenAiSettings()
    {
        _aiWindow?.Close();
        _aiWindow = new AiSettingsWindow(Ai, _sim.Talk.Enabled, on =>
        {
            _sim.Talk.Enabled = on;
            Save();
            PetsMenuChanged?.Invoke();
        }, ConnectClaudeCodeAsync, ServerInfo());
        _aiWindow.Closed += (_, _) => _aiWindow = null;
        _aiWindow.Show();
        _aiWindow.Activate();
    }

    // ---------------------------------------------------------------- coding agent buddy

    private void OnAgentEvent(string kind)
    {
        if (!Ai.Settings.AgentBuddy) return;
        var buddy = _sim.AgentEvent(kind);
        AppLog.Write($"Agent event '{kind}'{(buddy is null ? "" : $": {buddy.Name} reacts")}");
    }

    /// <summary>Adds PetFam's MCP server and the agent-buddy hooks to the user's Claude Code (backing up settings first).</summary>
    private async Task<string> ConnectClaudeCodeAsync()
    {
        if (Ai.Claude is not { } claude) return L.S("Claude Code isn't installed.", "Claude Code가 설치되어 있지 않아요.");
        var url = $"http://127.0.0.1:{Ai.Settings.Port}";
        var notes = new List<string>();
        try
        {
            await RunTool(claude.ExePath, "mcp", "remove", "--scope", "user", "petfam");
            var (code, output) = await RunTool(claude.ExePath, "mcp", "add", "--transport", "http", "--scope", "user", "petfam", url + "/mcp");
            notes.Add(code == 0 ? L.S("MCP server added.", "MCP 서버를 추가했어요.") : L.S($"MCP add failed: {output}", $"MCP 추가 실패: {output}"));
        }
        catch (Exception ex)
        {
            notes.Add("MCP: " + ex.Message);
        }

        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
            JsonObject root;
            if (File.Exists(path))
            {
                File.Copy(path, path + ".petfam-backup", overwrite: true);
                root = JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonObject ?? new JsonObject();
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                root = new JsonObject();
            }
            var hooks = root["hooks"] as JsonObject ?? new JsonObject();
            root["hooks"] = hooks;
            foreach (var (hookEvent, petEvent) in new[] { ("UserPromptSubmit", "working"), ("Stop", "done"), ("Notification", "waiting") })
            {
                var list = hooks[hookEvent] as JsonArray ?? new JsonArray();
                hooks[hookEvent] = list;
                // Drop PetFam's old entries (any port), then add the current one.
                foreach (var old in list.Where(e => e?.ToJsonString().Contains("/agent?event=") == true).ToList()) list.Remove(old);
                list.Add(new JsonObject
                {
                    ["hooks"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = $"curl -s -m 2 \"{url}/agent?event={petEvent}\"",
                    }),
                });
            }
            await File.WriteAllTextAsync(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            notes.Add(L.S("Hooks added (settings backed up to settings.json.petfam-backup).", "훅을 추가했어요 (설정은 settings.json.petfam-backup에 백업)."));
        }
        catch (Exception ex)
        {
            notes.Add(L.S("Hooks failed: ", "훅 추가 실패: ") + ex.Message);
        }
        AppLog.Write("Connect Claude Code: " + string.Join(" ", notes));
        return string.Join(" ", notes) + L.S(" Restart Claude Code to pick it up.", " Claude Code를 다시 시작하면 적용돼요.");
    }

    private static async Task<(int Code, string Output)> RunTool(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await p.WaitForExitAsync(cts.Token);
        return (p.ExitCode, ((await output) + " " + (await error)).Trim());
    }

    // ---------------------------------------------------------------- MCP tools

    private Pet? FindPet(JsonObject args)
    {
        var name = args["name"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrEmpty(name)) return _sim.Pets.Count == 1 ? _sim.Pets[0] : null;
        return _sim.Pets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? _sim.Pets.FirstOrDefault(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static Task<string> OnUi(Func<string> work) => Dispatcher.UIThread.InvokeAsync(work).GetTask();

    private static JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] props)
    {
        var p = new JsonObject();
        foreach (var x in props) p[x.Name] = new JsonObject { ["type"] = x.Type, ["description"] = x.Description };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = p,
            ["required"] = new JsonArray(props.Where(x => x.Required).Select(x => (JsonNode)x.Name).ToArray()),
        };
    }

    private string Status(Pet p)
    {
        var cell = _sim.World.Find(p.CellId);
        var n = p.Needs;
        return $"{p.Name}: {(p.Species == PetSpecies.Cat ? "cat" : "dog")}, {p.Breed.Name.En}, {Growth.StageName(p.Growth.Stage)}; " +
               $"{(p.Hidden ? "inside a house" : cell is null ? "somewhere" : _sim.World.DescribeCell(cell))}; doing: {p.Engine.Current?.Name ?? "nothing"}; " +
               $"hunger {n.Hunger:0}/100 (low = hungry), energy {n.Energy:0}, fun {n.Fun:0}, affection {n.Affection:0}; " +
               $"bond {p.Bond.Value:0} ({p.Bond.Stage}); mood {MoodCalc.Level(p)}" +
               (_sim.Journal.DiariesOf(p).FirstOrDefault() is { } d ? $"; latest diary ({d.Date:yyyy-MM-dd}): {d.Text}" : "");
    }

    private IReadOnlyList<McpTool> McpTools()
    {
        var name = ("name", "string", "The pet's name (optional when there is only one pet)", false);
        var nameRequired = ("name", "string", "The pet's name", true);
        var actions = string.Join(", ", InteractionCatalog.All.Select(i => i.Id));
        return new List<McpTool>
        {
            new("list_pets", "List the user's desktop pets with how each one is doing (needs, mood, bond, where, what it is doing).",
                Schema(), _ => OnUi(() => _sim.Pets.Count == 0 ? "No pets yet." : string.Join("\n", _sim.Pets.Select(Status)))),
            new("feed_pet", "Put a food bowl down in front of a pet.", Schema(name), a => OnUi(() =>
            {
                if (FindPet(a) is not { } p) return "No such pet.";
                _sim.Feed(p, DateTime.UtcNow);
                return $"Put a food bowl in front of {p.Name}.";
            })),
            new("ask_pet", $"Ask a pet to do something, Sims style (it may refuse if your bond is low). Actions: {actions}.",
                Schema(nameRequired, ("action", "string", "One of: " + actions, true)), a => OnUi(() =>
                {
                    if (FindPet(a) is not { } p) return "No such pet.";
                    var id = a["action"]?.GetValue<string>();
                    var i = InteractionCatalog.All.FirstOrDefault(x => x.Id == id);
                    if (i is null) return $"Unknown action. Use one of: {actions}.";
                    var cell = _sim.World.Find(p.CellId) ?? _cell!;
                    var check = i.Check(p, cell, DateTime.UtcNow);
                    if (!check.Enabled) return $"{p.Name} can't do that now{(check.Reason is null ? "" : ": " + check.Reason)}.";
                    return _sim.Enqueue(p, i, DateTime.UtcNow) ? $"Queued '{i.Label}' for {p.Name}." : $"{p.Name}'s queue is full.";
                })),
            new("pet_says", "Make a pet say something in a speech bubble on the desktop (keep it short and in character).",
                Schema(nameRequired, ("text", "string", "What it says (up to 60 characters)", true)), a => OnUi(() =>
                {
                    if (FindPet(a) is not { } p) return "No such pet.";
                    var text = a["text"]?.GetValue<string>() ?? "";
                    if (text.Length > 60) text = text[..60];
                    p.Say(text, 4f);
                    return $"{p.Name} says: {text}";
                })),
            new("call_pet", "Call a pet over to the desktop the user is on now (it comes if it trusts you enough).", Schema(nameRequired), a => OnUi(() =>
            {
                if (FindPet(a) is not { } p) return "No such pet.";
                return _sim.CallPet(p) ? $"{p.Name} is on its way." : $"{p.Name} didn't come (already here, or not close enough to you yet).";
            })),
            new("read_diary", "Read a pet's diary (what it wrote about its days), newest first.",
                Schema(name, ("days", "integer", "How many entries (default 7)", false)), a => OnUi(() =>
                {
                    var pets = FindPet(a) is { } one ? new[] { one } : _sim.Pets.ToArray();
                    var days = a["days"]?.GetValue<int>() ?? 7;
                    var text = string.Join("\n\n", pets.Select(p =>
                    {
                        var entries = _sim.Journal.DiariesOf(p).Take(days).ToList();
                        return $"# {p.Name}\n" + (entries.Count == 0 ? "(no diary yet)" : string.Join("\n", entries.Select(e => $"{e.Date:yyyy-MM-dd}: {e.Text}")));
                    }));
                    return text.Length == 0 ? "No pets yet." : text;
                })),
            new("weekly_story", "Read the latest weekly story about the pets.", Schema(), _ => OnUi(() =>
                _sim.Journal.Stories.OrderByDescending(s => s.Date).FirstOrDefault() is { } s ? $"{s.Title} ({s.Date:yyyy-MM-dd})\n{s.Text}" : "No story yet.")),
        };
    }

    // ---------------------------------------------------------------- diaries and weekly stories

    private bool _diaryBusy;
    private readonly HashSet<(Guid, DateOnly)> _diaryTried = new();
    private DateOnly _storyTried;

    /// <summary>PETFAM_DIARY_NOW=1: write diaries right away instead of after 9 pm (testing).</summary>
    private static readonly bool DiaryNow = Environment.GetEnvironmentVariable("PETFAM_DIARY_NOW") == "1";

    /// <summary>Once a minute: at night (21:00 on), or for days gone by, a pet with a day worth writing about writes its diary.</summary>
    private async Task DiaryTickAsync()
    {
        if (!Ai.Available || !Ai.Settings.Diary || _diaryBusy) return;
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var log = _sim.Journal.Days
            .Where(d => !d.Empty && (d.Date < today || now.Hour >= 21 || DiaryNow) && !_diaryTried.Contains((d.PetId, d.Date)))
            .Where(d => _sim.Pets.Any(p => p.Id == d.PetId))
            .FirstOrDefault(d => _sim.Journal.Diaries.All(e => e.PetId != d.PetId || e.Date != d.Date));
        if (log is not null)
        {
            _diaryTried.Add((log.PetId, log.Date));
            await WriteDiaryAsync(_sim.Pets.First(p => p.Id == log.PetId), log.Date);
            return;
        }
        var last = _sim.Journal.Stories.Count == 0 ? DateOnly.MinValue : _sim.Journal.Stories.Max(s => s.Date);
        if (now.Hour >= 21 && _storyTried != today && last <= today.AddDays(-7) &&
            _sim.Journal.Diaries.Count(d => d.Date > today.AddDays(-7)) >= 3)
        {
            _storyTried = today;
            await WriteStoryAsync();
        }
    }

    /// <summary>The pet writes its diary for that day (in its own voice). Null if the AI didn't answer.</summary>
    public async Task<string?> WriteDiaryAsync(Pet pet, DateOnly date)
    {
        var log = _sim.Journal.Day(pet, date);
        var before = _sim.Journal.DiariesOf(pet).FirstOrDefault(d => d.Date < date);
        var prompt = new StringBuilder()
            .AppendLine($"Write {pet.Name}'s diary entry for {date:yyyy-MM-dd} ({date.DayOfWeek}).")
            .AppendLine()
            .AppendLine("Who it is: " + PetTalk.Persona(pet))
            .AppendLine()
            .AppendLine("Its day:")
            .AppendLine(log.Empty ? "A quiet day: mostly lazing about." : log.Describe(_ => null))
            .AppendLine(before is null ? "" : $"Its previous entry ({before.Date:yyyy-MM-dd}): {before.Text}")
            .AppendLine()
            .AppendLine("First person, in its own voice and personality (a cat and a dog write very differently; a baby writes clumsily). " +
                        "2 to 4 short sentences about one or two real things from its day. " +
                        (L.Ko ? "Korean, casual speech (반말). Call the owner 집사. " : "English, casual. Call the owner \"my human\". ") +
                        "Plain text only: no title, no date, no emoji, no quotes.")
            .ToString();
        _diaryBusy = true;
        try
        {
            var answer = await Ai.AskAsync("You write diary entries for pets in a cozy desktop pet game.", prompt);
            var text = Clean(answer, 420);
            if (text is null) return null;
            _sim.Journal.Write(pet, date, text);
            AppLog.Write($"Diary of {pet.Name} for {date:yyyy-MM-dd}: {text}");
            Save();
            _panel?.Refresh();
            return text;
        }
        finally
        {
            _diaryBusy = false;
        }
    }

    /// <summary>A weekly story about all the pets, from their diaries of the last 7 days and how they get on.</summary>
    public async Task<StoryEntry?> WriteStoryAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var diaries = _sim.Journal.Diaries.Where(d => d.Date > today.AddDays(-7)).OrderBy(d => d.Date).ToList();
        if (diaries.Count == 0) return null;
        string NameOf(Guid id) => _sim.Pets.FirstOrDefault(p => p.Id == id)?.Name ?? "?";
        var sb = new StringBuilder();
        sb.AppendLine("The pets:");
        foreach (var p in _sim.Pets) sb.AppendLine("- " + PetTalk.Persona(p));
        sb.AppendLine();
        sb.AppendLine("How they get on (friendship 0-100):");
        for (var i = 0; i < _sim.Pets.Count; i++)
        for (var j = i + 1; j < _sim.Pets.Count; j++)
            sb.AppendLine($"- {_sim.Pets[i].Name} & {_sim.Pets[j].Name}: {_sim.Social.Friendship(_sim.Pets[i], _sim.Pets[j]):0}");
        sb.AppendLine();
        sb.AppendLine("Their diaries this week:");
        foreach (var d in diaries) sb.AppendLine($"- {d.Date:MM-dd} {NameOf(d.PetId)}: {d.Text}");
        sb.AppendLine();
        sb.AppendLine("Write this week's episode of their story, like a cozy comic strip: a short title and 5 to 8 sentences, " +
                      "built from what really happened in the diaries, with each pet true to its personality. " +
                      (L.Ko ? "Korean. " : "English. ") + "Reply with JSON only: {\"title\":\"...\",\"text\":\"...\"}");
        var answer = await Ai.AskAsync("You write a weekly story about pets in a cozy desktop pet game.", sb.ToString(), TimeSpan.FromSeconds(120));
        if (Json(answer) is not JsonObject o || o["text"]?.GetValue<string>() is not { Length: > 0 } text) return null;
        var story = new StoryEntry { Date = today, Title = Clean(o["title"]?.GetValue<string>(), 80) ?? L.S("This week", "이번 주"), Text = text.Trim() };
        _sim.Journal.Stories.Add(story);
        AppLog.Write($"Weekly story: {story.Title}");
        Save();
        return story;
    }

    private DiaryWindow? _diaryWindow;

    /// <summary>Panel / tray: the diaries and weekly stories.</summary>
    public void OpenDiary(Pet? pet = null)
    {
        _diaryWindow?.Close();
        _diaryWindow = new DiaryWindow(_sim.Pets, _sim.Journal, pet, Ai.Available,
            p => WriteDiaryAsync(p, DateOnly.FromDateTime(DateTime.Now)), WriteStoryAsync);
        _diaryWindow.Closed += (_, _) => _diaryWindow = null;
        _diaryWindow.Show();
        _diaryWindow.Activate();
    }

    // ---------------------------------------------------------------- talking with a pet

    private readonly Dictionary<Guid, List<string>> _chatMemory = new();
    private PetChatWindow? _chat;

    /// <summary>Pie menu "Talk": a little chat box beside the pet.</summary>
    public void OpenChat(Pet pet)
    {
        _chat?.Close();
        _chat = new PetChatWindow(pet, _chatMemory.GetValueOrDefault(pet.Id) ?? new List<string>(), msg => ChatAsync(pet, msg));
        _chat.Closed += (_, _) => _chat = null;
        _chat.Show();
        _chat.Activate();
    }

    /// <summary>The pet answers in character, and may do something it was asked (from what it can do right now).</summary>
    public async Task<string?> ChatAsync(Pet pet, string message)
    {
        var memory = _chatMemory.TryGetValue(pet.Id, out var m) ? m : _chatMemory[pet.Id] = new List<string>();
        var options = _sim.MenuFor(pet, DateTime.UtcNow).Where(x => x.Availability.Enabled).Select(x => x.Interaction).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Who you are: " + PetTalk.Persona(pet));
        sb.AppendLine($"Your bond with the owner is '{pet.Bond.Stage}' ({pet.Bond.Value:0}/100): " +
                      (pet.Bond.Value < 30 ? "you are still wary of them; short answers." : pet.Bond.Value < 70 ? "you like them." : "they are your whole world."));
        if (_sim.Journal.DiariesOf(pet).FirstOrDefault() is { } diary) sb.AppendLine($"Your latest diary entry: {diary.Text}");
        if (memory.Count > 0) sb.AppendLine("The conversation so far:\n" + string.Join("\n", memory.TakeLast(10)));
        sb.AppendLine();
        sb.AppendLine("Things you could do now if you want to (or not): " + (options.Count == 0 ? "nothing" : string.Join(", ", options.Select(i => $"{i.Id} ({i.Label})"))));
        sb.AppendLine();
        sb.AppendLine($"The owner says: {message}");
        sb.AppendLine();
        sb.AppendLine($"Answer as {pet.Name}, in character, at most {PetTalk.MaxChars} characters. " +
                      (L.Ko ? "Korean, casual speech (반말) like a pet would; call the owner 집사. " : "English, casual; call the owner \"my human\". ") +
                      "Pet sounds welcome, no emoji, no stage directions. If the owner asks you to do one of those things and you are willing, put its id in \"do\". " +
                      "Reply with JSON only: {\"reply\":\"...\",\"do\":null}");
        var answer = await Ai.AskAsync($"You are {pet.Name}, a pet in a cozy desktop pet game. Stay in character. Reply with JSON only.", sb.ToString(), TimeSpan.FromSeconds(60));
        if (Json(answer) is not JsonObject o) return null;
        var reply = PetTalk.StripDirections(o["reply"]?.GetValue<string>());
        if (string.IsNullOrEmpty(reply)) return null;
        memory.Add($"Owner: {message}");
        memory.Add($"{pet.Name}: {reply}");
        if (memory.Count > 20) memory.RemoveRange(0, memory.Count - 20);
        pet.Say(reply, PetFam.Core.Talk.TalkSession.ReadSeconds(reply) + 1f);
        _sim.Journal.Owner(pet, "chatted with it", DateTime.UtcNow);
        var did = "";
        if (o["do"] is JsonValue v && v.TryGetValue<string>(out var id) && options.FirstOrDefault(i => i.Id == id) is { } act &&
            _sim.Enqueue(pet, act, DateTime.UtcNow))
            did = $"  ({act.Icon} {act.Label})";
        AppLog.Write($"Chat with {pet.Name}: \"{message}\" -> \"{reply}\"{did}");
        return reply + did;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The JSON object in an AI answer (it may have text or a code fence around it), or null.</summary>
    internal static JsonNode? Json(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try { return JsonNode.Parse(answer[start..(end + 1)]); }
        catch (JsonException) { return null; }
    }

    /// <summary>Plain text from an AI answer: no surrounding quotes or markdown, cut to a length. Null if empty.</summary>
    internal static string? Clean(string? answer, int max)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;
        var t = PetTalk.StripDirections(answer.Trim().Trim('"', '“', '”', '`').Replace("**", ""))!.Trim();
        if (t.StartsWith('#')) t = string.Join("\n", t.Split('\n').Skip(1)).Trim();
        if (t.Length == 0) return null;
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }
}
