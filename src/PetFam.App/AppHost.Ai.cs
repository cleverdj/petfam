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
using PetFam.Desktop.Rendering;
using PetFam.Core.World;
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
        Ai.Changed += ReadAllWallpapers;
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
            .AppendLine(_sim.Scenery.For(pet.CellId) is { } place ? $"Where it lives now (the scenery around it): {place.Now}" : "")
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
        if (_sim.Scenery.For(pet.CellId) is { } place)
            sb.AppendLine($"Where you live (the scenery around you): {place.Now}" + (place.Before is null ? "" : $" (before, you lived in: {place.Before})") +
                          ". To you it is simply your world, not a wallpaper.");
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

    // ---------------------------------------------------------------- making things from words (and photos)

    private static string Lang => L.Ko ? "Korean" : "English";

    /// <summary>House editor: shape, colours, decorations (and a name) from a description. Applies to <paramref name="work"/>.</summary>
    public async Task<string?> DesignHouseAsync(string words, PetHouse work)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"The owner wants a pet house like this: \"{words}\"");
        sb.AppendLine("Choose from what exists:");
        sb.AppendLine("shape: " + string.Join(", ", HouseArt.Shapes.Select(x => $"{x.Shape} ({x.Name.En})")));
        sb.AppendLine("roof (index): " + string.Join(", ", HouseArt.RoofColors.Select((c, i) => $"{i} {c.Name.En}")));
        sb.AppendLine("wall (index): " + string.Join(", ", HouseArt.WallColors.Select((c, i) => $"{i} {c.Name.En}")));
        sb.AppendLine("decorations (any number): " + string.Join(", ", HouseArt.Decorations.Select(d => $"{d.Id} ({d.Name.En})")));
        sb.AppendLine($"Also a cute house name in {Lang}, at most 8 characters, or null to keep \"{work.Name}\".");
        sb.AppendLine("Reply with JSON only: {\"shape\":\"Gable\",\"roof\":0,\"wall\":0,\"decorations\":[\"flowers\"],\"name\":null,\"why\":\"one short sentence\"}");
        if (Json(await Ai.AskAsync("You design pet houses for a cozy desktop pet game from a fixed set of parts. Reply with JSON only.", sb.ToString(), TimeSpan.FromSeconds(60))) is not JsonObject o) return null;
        if (Enum.TryParse<HouseShape>(o["shape"]?.ToString(), true, out var shape)) work.Shape = shape;
        if (o["roof"] is JsonValue r && r.TryGetValue<int>(out var roof)) work.RoofColor = Math.Clamp(roof, 0, HouseArt.RoofColors.Length - 1);
        if (o["wall"] is JsonValue w && w.TryGetValue<int>(out var wall)) work.WallColor = Math.Clamp(wall, 0, HouseArt.WallColors.Length - 1);
        if (o["decorations"] is JsonArray decos)
            work.Decorations = decos.Select(d => d?.ToString() ?? "").Where(id => HouseArt.Decorations.Any(x => x.Id == id)).ToHashSet();
        if (o["name"] is JsonValue n && n.TryGetValue<string>(out var name) && name.Trim().Length is > 0 and <= 12) work.Name = name.Trim();
        AppLog.Write($"AI house from \"{words}\": {work.Shape}, roof {work.RoofColor}, wall {work.WallColor}, {string.Join(",", work.Decorations)}");
        return o["why"]?.ToString() ?? L.S("Done!", "골랐어요!");
    }

    /// <summary>Dress-up: picks unlocked pieces and their colours from a description ("Christmas look"). Applies to <paramref name="work"/>.</summary>
    public async Task<string?> DressFromWordsAsync(Pet pet, string words, Outfit work)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"The owner wants to dress {pet.Name} ({pet.Breed.Name.En}, {(pet.Species == PetSpecies.Cat ? "cat" : "dog")}) like this: \"{words}\"");
        sb.AppendLine("Pieces it can wear now, per slot (id: name, colours):");
        foreach (var slot in Enum.GetValues<OutfitSlot>())
        {
            var items = OutfitCatalog.Of(slot).Where(i => OutfitCatalog.IsUnlocked(pet, i)).ToList();
            sb.AppendLine($"{slot.ToString().ToLowerInvariant()}: " + (items.Count == 0 ? "(nothing yet)" :
                string.Join("; ", items.Select(i => $"{i.Id}: {i.Name.En} [{string.Join(" ", i.Colors.Select(c => $"#{c:X6}"))}]"))));
        }
        sb.AppendLine("For each slot pick one piece and one of its colours, or null for nothing. Only use ids and colours listed above.");
        sb.AppendLine($"Reply with JSON only: {{\"head\":{{\"id\":\"...\",\"color\":\"#RRGGBB\"}},\"neck\":null,\"body\":null,\"face\":null,\"why\":\"one short sentence in {Lang}\"}}");
        if (Json(await Ai.AskAsync("You are a pet stylist in a cozy desktop pet game, choosing from a fixed wardrobe. Reply with JSON only.", sb.ToString(), TimeSpan.FromSeconds(60))) is not JsonObject o) return null;
        var picked = 0;
        foreach (var slot in Enum.GetValues<OutfitSlot>())
        {
            if (!o.TryGetPropertyValue(slot.ToString().ToLowerInvariant(), out var node)) continue;
            if (node is not JsonObject piece) { work.Set(slot, null); continue; }
            var item = OutfitCatalog.Find(piece["id"]?.ToString());
            if (item is null || item.Slot != slot || !OutfitCatalog.IsUnlocked(pet, item)) continue;
            var color = Hex(piece["color"]?.ToString()) is { } c ? Nearest(item.Colors, c) : item.Colors[0];
            work.Set(slot, new OutfitPiece(item.Id, color));
            picked++;
        }
        AppLog.Write($"AI outfit for {pet.Name} from \"{words}\": {picked} piece(s)");
        return picked == 0 ? L.S("Nothing it has unlocked fits that yet.", "아직 열린 아이템 중엔 어울리는 게 없어요.") : o["why"]?.ToString() ?? L.S("Done!", "골랐어요!");
    }

    /// <summary>Coat colours from a description ("cream with brown ears") or a photo of the user's own pet.</summary>
    public async Task<(PetCoat Coat, string Why)?> CoatFromAsync(Pet pet, string? words, string? photo)
    {
        if (photo is not null && Ai.Writer?.CanSee != true) return null;
        var prompt = new StringBuilder()
            .AppendLine($"Pick coat colours for {pet.Name}, a {pet.Breed.Name.En} ({(pet.Species == PetSpecies.Cat ? "cat" : "dog")}) drawn as a cute 3D toy.")
            .AppendLine(photo is not null ? "Match the pet in the photo as closely as you can." : "")
            .AppendLine(words is not null ? $"The owner says: \"{words}\"" : "")
            .AppendLine("fur = main coat; light = belly, chest, muzzle and paws; marking = patches, ears, tail tip, stripes; eyes = iris colour.")
            .AppendLine($"Reply with JSON only: {{\"fur\":\"#RRGGBB\",\"light\":\"#RRGGBB\",\"marking\":\"#RRGGBB\",\"eyes\":\"#RRGGBB\",\"why\":\"one short sentence in {Lang}\"}}")
            .ToString();
        if (Json(await Ai.AskAsync("You choose colours for pets in a cozy desktop pet game. Reply with JSON only.", prompt, TimeSpan.FromSeconds(90), photo)) is not JsonObject o) return null;
        var coat = new PetCoat(Hex(o["fur"]?.ToString()), Hex(o["light"]?.ToString()), Hex(o["marking"]?.ToString()), Hex(o["eyes"]?.ToString()));
        if (coat.IsEmpty) return null;
        AppLog.Write($"AI coat for {pet.Name} from {(photo is null ? $"\"{words}\"" : "a photo")}: {coat}");
        return (coat, o["why"]?.ToString() ?? L.S("Done!", "골랐어요!"));
    }

    /// <summary>Adoption: a breed, a name and coat colours from a description and/or a photo of the user's real pet.</summary>
    public async Task<(Breed Breed, string Name, PetCoat? Coat, string Why)?> PickPetAsync(string? words, string? photo)
    {
        if (photo is not null && Ai.Writer?.CanSee != true) return null;
        var sb = new StringBuilder();
        sb.AppendLine("Help the owner adopt a pet for a desktop pet game.");
        if (photo is not null) sb.AppendLine("The photo shows their real pet: pick the closest breed and match its colours.");
        if (words is not null) sb.AppendLine($"The owner says: \"{words}\"");
        sb.AppendLine("Breeds (id: name, species - temperament):");
        foreach (var b in Breed.All) sb.AppendLine($"{b.Id}: {b.Name.En}, {(b.Species == PetSpecies.Cat ? "cat" : "dog")} - {b.Tagline.En}");
        sb.AppendLine($"Also a name in {Lang} (at most 8 characters) and coat colours (fur = main coat; light = belly and paws; marking = patches, ears, tail; eyes = iris), or null to keep the breed's.");
        sb.AppendLine($"Reply with JSON only: {{\"breed\":\"id\",\"name\":\"...\",\"fur\":null,\"light\":null,\"marking\":null,\"eyes\":null,\"why\":\"one short sentence in {Lang}\"}}");
        if (Json(await Ai.AskAsync("You match people with pets in a cozy desktop pet game. Reply with JSON only.", sb.ToString(), TimeSpan.FromSeconds(90), photo)) is not JsonObject o) return null;
        var breed = Breed.All.FirstOrDefault(b => b.Id == o["breed"]?.ToString());
        if (breed is null) return null;
        var name = o["name"]?.ToString()?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 12) name = breed.Names[0].Text;
        var coat = new PetCoat(Hex(o["fur"]?.ToString()), Hex(o["light"]?.ToString()), Hex(o["marking"]?.ToString()), Hex(o["eyes"]?.ToString())).OrNull();
        AppLog.Write($"AI adoption pick from {(photo is null ? $"\"{words}\"" : "a photo")}: {breed.Id} '{name}'{(coat is null ? "" : $" {coat}")}");
        return (breed, name, coat, o["why"]?.ToString() ?? "");
    }

    private static uint? Hex(string? s)
    {
        if (s is null) return null;
        s = s.Trim().TrimStart('#');
        return s.Length == 6 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : null;
    }

    private static uint Nearest(uint[] colors, uint want)
    {
        static int D(uint a, uint b)
        {
            int dr = (int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF), dg = (int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF), db = (int)(a & 0xFF) - (int)(b & 0xFF);
            return dr * dr + dg * dg + db * db;
        }
        return colors.OrderBy(c => D(c, want)).First();
    }

    /// <summary>PETFAM_AI_SELFTEST=1: runs each "from words" job once against the real AI and logs what came back (nothing is applied).</summary>
    private async Task AiSelfTestAsync()
    {
        if (!Ai.Available || _sim.Pets.Count == 0) { AppLog.Write("AI self-test: no AI or no pets"); return; }
        var pet = _sim.Pets[0];
        var house = new PetHouse { Name = "test" };
        AppLog.Write("AI self-test house: " + await DesignHouseAsync(L.Ko ? "등불 달린 아늑한 통나무집" : "a cosy log cabin with a lantern", house) + $" -> {house.Shape} {house.RoofColor} {house.WallColor} {string.Join(",", house.Decorations)} '{house.Name}'");
        var outfit = new Outfit();
        AppLog.Write("AI self-test outfit: " + await DressFromWordsAsync(pet, L.Ko ? "크리스마스 파티" : "Christmas party", outfit) + " -> " + string.Join(", ", outfit.Pieces.Select(kv => $"{kv.Key}={kv.Value.ItemId}#{kv.Value.Color:X6}")));
        AppLog.Write("AI self-test coat: " + (await CoatFromAsync(pet, L.Ko ? "크림색에 귀만 갈색" : "cream with brown ears", null))?.ToString());
        AppLog.Write("AI self-test adopt: " + (await PickPetAsync(L.Ko ? "겁 많고 조용한 하얀 고양이" : "a shy quiet white cat", null))?.ToString());
        if (Environment.GetEnvironmentVariable("PETFAM_AI_SELFTEST_PHOTO") is { } photo && File.Exists(photo)) AppLog.Write("AI self-test coat from a picture: " + (await CoatFromAsync(pet, null, photo))?.ToString());
        if (Environment.GetEnvironmentVariable("PETFAM_AI_SELFTEST_WALLPAPER") is { } wall && File.Exists(wall) && _cell is { } c0)
        {
            using var pic = new Avalonia.Media.Imaging.Bitmap(wall);
            var view = ScreenView(pic, c0.Bounds.Width, c0.Bounds.Height);
            AppLog.Write($"AI self-test: screen view of {wall} saved to {view}");
            var dabs = await ReadWallpaperAsync(c0, pic);
            AppLog.Write($"AI self-test test wallpaper: {dabs?.Count.ToString() ?? "no answer"} dabs");
        }
        if (_cell is { } cell && WallpaperPath() is { } wp && File.Exists(wp))
        {
            using var picture = new Avalonia.Media.Imaging.Bitmap(wp);
            var strokes = await ReadWallpaperAsync(cell, picture);
            AppLog.Write($"AI self-test wallpaper: {strokes?.Count.ToString() ?? "no answer"} dabs");
        }
        AppLog.Write("AI self-test done");
    }

    // ---------------------------------------------------------------- reading the wallpaper

    private static string AiHabitatPath => Path.Combine(AppLog.DataDirectory, "habitat-ai.json");
    private HabitatEdits? _aiEdits;

    /// <summary>The AI's reading of each wallpaper (per wallpaper and screen size), kept so it is asked only once.</summary>
    private HabitatEdits AiEdits => _aiEdits ??= HabitatEdits.Read(AiHabitatPath);

    /// <summary>The wallpaper file each analysed cell shows (not for PetFam's own wallpapers: those are exact).</summary>
    private readonly Dictionary<string, string> _cellWallpaper = new();
    private readonly HashSet<string> _aiReading = new();

    private static string AiSceneryPath => Path.Combine(AppLog.DataDirectory, "scenery-ai.json");
    private SceneryFile? _scenery;

    /// <summary>The AI's few words about each wallpaper, and what each screen showed last (to notice a change after a restart).</summary>
    private SceneryFile Sceneries => _scenery ??= SceneryFile.Read(AiSceneryPath);

    private static string? OfficialScene(string id) => id switch
    {
        "sunny-meadow" => "a sunny green meadow of soft grass and wildflowers under a wide blue sky",
        "seaside" => "a seaside: a sandy beach, gentle blue waves and a bright sky",
        "cozy-garden" => "a cozy garden with flowerbeds, a little path, shady trees and a pond",
        _ => null,
    };

    /// <summary>
    /// The pets on this screen now live in this scenery. A different wallpaper from the last one (even across a restart)
    /// is a change they will notice and talk about.
    /// </summary>
    private void ShowScene(Cell cell, string wallpaperKey, string text)
    {
        var last = Sceneries.Last.GetValueOrDefault(cell.Id);
        var before = last is not null && last.Key != wallpaperKey ? last.Text : null;
        if (_sim.Scenery.Set(cell.Id, wallpaperKey, text, before))
        {
            AppLog.Write($"Scenery on {_sim.World.DescribeCell(cell)} changed: {_sim.Scenery.For(cell.Id)?.Before} -> {text}");
            foreach (var pet in _sim.Pets.Where(p => p.CellId == cell.Id))
                _sim.Journal.Owner(pet, "moved us to a new place: " + text, DateTime.UtcNow);
        }
        else AppLog.Write($"Scenery on {_sim.World.DescribeCell(cell)}: {text}");
        Sceneries.Last[cell.Id] = new SceneryFile.Seen { Key = wallpaperKey, Text = text };
        Sceneries.Write(AiSceneryPath);
    }

    /// <summary>Every analysed wallpaper the AI hasn't read yet (the AI has just been set up, or changed).</summary>
    private void ReadAllWallpapers()
    {
        foreach (var cell in _sim.World.Cells) MaybeReadWallpaper(cell);
    }

    /// <summary>Tray "Re-detect habitat": the AI reads it again too.</summary>
    private void ForgetAiReading(Cell cell)
    {
        var key = EditKey(cell);
        _aiReading.Remove(key);
        if (Sceneries.Scenes.Remove(key)) Sceneries.Write(AiSceneryPath);
        if (AiEdits.For(key).Count == 0) return;
        AiEdits.Set(key, Array.Empty<HabitatStroke>());
        try { AiEdits.Write(AiHabitatPath); } catch (IOException) { }
    }

    /// <summary>
    /// Once per wallpaper (and screen size): if the AI can see pictures and the setting is on, it reads the wallpaper
    /// in the background and its reading is laid over the automatic analysis (the user's own fixes stay on top).
    /// </summary>
    private void MaybeReadWallpaper(Cell cell)
    {
        if (!_cellWallpaper.TryGetValue(cell.Id, out var path) || !File.Exists(path)) return;
        var key = EditKey(cell);
        // Read before: the pets know where they are without asking again.
        if (Sceneries.Scenes.GetValueOrDefault(key) is { } known)
        {
            ShowScene(cell, _analyzedKeys.GetValueOrDefault(cell.Id) ?? key, known);
            return;
        }
        if (!Ai.Settings.AutoHabitat || Ai.Writer?.CanSee != true) return;
        if (!_aiReading.Add(key)) return;
        _ = ReadAndApplyAsync(cell, path, key);
    }

    private async Task ReadAndApplyAsync(Cell cell, string path, string key)
    {
        IReadOnlyList<HabitatStroke>? strokes;
        string? scene = null;
        try
        {
            using var picture = new Avalonia.Media.Imaging.Bitmap(path);
            AppLog.Write($"AI habitat: reading the wallpaper on {_sim.World.DescribeCell(cell)}");
            strokes = await ReadWallpaperAsync(cell, picture, s => scene = s);
        }
        catch (Exception ex)
        {
            AppLog.Write("AI habitat: " + ex.Message);
            return;
        }
        if (EditKey(cell) != key) { AppLog.Write("AI habitat: the wallpaper changed meanwhile; dropped"); return; }
        if (scene is not null)
        {
            Sceneries.Scenes[key] = scene;
            ShowScene(cell, _analyzedKeys.GetValueOrDefault(cell.Id) ?? key, scene);
        }
        if (strokes is null) return;
        AiEdits.Set(key, strokes);
        try { AiEdits.Write(AiHabitatPath); }
        catch (IOException ex) { AppLog.Write("AI habitat not saved: " + ex.Message); }
        ApplyHabitat(cell, _autoMasks.GetValueOrDefault(cell.Id), $"the AI ({Ai.Writer?.Name}) read the wallpaper");
    }

    public const int GridCols = 24;

    /// <summary>
    /// Habitat editor: the AI looks at the wallpaper as this screen shows it and labels a grid (floor, water, sand,
    /// mud, sun, shade, not-floor, sky); the grid becomes brush dabs the user can check and fix before saving.
    /// </summary>
    public async Task<IReadOnlyList<HabitatStroke>?> ReadWallpaperAsync(Cell cell, Avalonia.Media.Imaging.Bitmap picture, Action<string>? scene = null)
    {
        var b = cell.Bounds;
        var aspect = b.Height / (float)Math.Max(1, b.Width);
        var rows = Math.Clamp((int)Math.Round(GridCols * aspect), 6, 30);
        string file;
        try { file = ScreenView(picture, b.Width, b.Height); }
        catch (Exception ex) { AppLog.Write("Read wallpaper: " + ex.Message); return null; }
        try
        {
            var prompt = $$"""
                This is a computer's desktop wallpaper. Small pets will live on it: they walk on floors, swim in water, nap in sunny spots and hide in shade.
                Divide the picture into a grid of {{GridCols}} columns and {{rows}} rows and label every cell with one letter:
                G = floor or ground they can walk on (grass, earth, paths, floors, the top of flat surfaces seen from above)
                W = water (sea, lake, river, pool)
                S = sand or beach
                M = mud
                U = a sunny, warm patch of ground
                H = shade or shelter on the ground (under trees, porches, bushes)
                B = not walkable (walls, buildings, trunks, rocks faces, furniture fronts, anything upright)
                K = sky or far-away background
                The pets must have somewhere to walk: about a fifth of the cells or more should be G (or S, M, U, H).
                If the picture has no obvious ground (a seascape, a cityscape, a close-up, an abstract picture), pick the most
                plausible surfaces as floor: a shore, the tops of rocks, a road or pier, a window sill, or else the bottom
                strip of the picture. Water a pet could swim in stays W.
                Also describe the place in "scene": 10 to 25 words of plain English, as the place the pets live in, naming the main
                things a small animal would notice (mountains, sea, a lake, trees, flowers, buildings, snow, the sky, the light,
                the season or time of day). No mention of a picture, wallpaper or screen.
                Reply with JSON only: {"scene":"...","rows":["{{new string('G', GridCols)}}", ...]} with exactly {{rows}} strings of exactly {{GridCols}} letters, top row first.
                """;
            var answer = await Ai.AskAsync("You label pictures for a desktop pet game's habitat map. Reply with JSON only.", prompt, TimeSpan.FromSeconds(120), file);
            var json = Json(answer);
            if (Clean(json?["scene"] is JsonValue sv && sv.TryGetValue<string>(out var st) ? st : null, 200) is { } words) scene?.Invoke(words);
            if (json?["rows"] is not JsonArray arr) return null;
            var grid = arr.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList();
            if (grid.Count < rows / 2) return null;
            // Pets need somewhere to stand: a reading with hardly any floor is a misreading, not a habitat.
            var cells = grid.Sum(r => r.Length);
            var standable = grid.Sum(r => r.Count(ch => "GSMUHgsmuh".Contains(ch)));
            if (standable < cells * 0.08)
            {
                AppLog.Write($"AI habitat: only {standable} of {cells} cells are floor; keeping the automatic analysis\n  " + string.Join("\n  ", grid));
                return null;
            }
            var strokes = HabitatEdits.FromGrid(grid, aspect);
            AppLog.Write($"AI read the wallpaper on {_sim.World.DescribeCell(cell)}: {grid.Count}x{grid.Max(r => r.Length)} grid, {strokes.Count} dabs\n  " + string.Join("\n  ", grid));
            return strokes;
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    /// <summary>The wallpaper as the screen shows it (cover-fit, centred), at most 1280 px wide, saved as a PNG.</summary>
    private static string ScreenView(Avalonia.Media.Imaging.Bitmap picture, int screenW, int screenH)
    {
        var pw = picture.PixelSize.Width;
        var ph = picture.PixelSize.Height;
        var k = Math.Max(screenW / (double)pw, screenH / (double)ph);   // cover
        var srcW = screenW / k;
        var srcH = screenH / k;
        var src = new Avalonia.Rect((pw - srcW) / 2, (ph - srcH) / 2, srcW, srcH);
        var outW = Math.Min(1280, screenW);
        var outH = Math.Max(1, (int)(outW * screenH / (double)screenW));
        using var target = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(outW, outH));
        using (var ctx = target.CreateDrawingContext())
            ctx.DrawImage(picture, src, new Avalonia.Rect(0, 0, outW, outH));
        var dir = Path.Combine(Path.GetTempPath(), "PetFam");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"wallpaper-{Guid.NewGuid():N}.png");
        target.Save(path);
        return path;
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
