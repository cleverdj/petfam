using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using PetFam.Core.Talk;

namespace PetFam.Desktop.Ai;

public enum AiProvider
{
    /// <summary>The first one found: Claude CLI, Codex CLI, Gemini CLI, Ollama, then the API key.</summary>
    Auto,
    ClaudeCli,
    CodexCli,
    GeminiCli,
    Ollama,
    AnthropicApi,
    Off,
}

/// <summary>AI settings, kept in their own file next to the save (the API key never goes into save.json).</summary>
public sealed class AiSettings
{
    public AiProvider Provider { get; set; } = AiProvider.Auto;
    public string? OllamaModel { get; set; }
    public string? AnthropicKey { get; set; }

    /// <summary>Pets write a diary at night (one short call per pet a day).</summary>
    public bool Diary { get; set; } = true;

    /// <summary>The local server for MCP tools and coding-agent events (127.0.0.1 only).</summary>
    public bool LocalServer { get; set; } = true;
    public int Port { get; set; } = AiServer.DefaultPort;

    /// <summary>Pets react to the user's coding agent (Claude Code hooks) through the local server.</summary>
    public bool AgentBuddy { get; set; } = true;
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AiSettings))]
internal partial class AiJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Which AI PetFam talks to: finds what the user has (a signed-in CLI, Ollama, an API key), keeps the settings,
/// and hands the chosen writer to the features (pet talk, chat, diary, making things from words, pictures).
/// </summary>
public sealed class AiHub
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetFam", "ai.json");

    public AiSettings Settings { get; private set; } = new();

    /// <summary>What was found on this computer (refreshed by <see cref="DetectAsync"/>).</summary>
    public ClaudeCliWriter? Claude { get; private set; }
    public CodexCliWriter? Codex { get; private set; }
    public GeminiCliWriter? Gemini { get; private set; }
    public IReadOnlyList<string>? OllamaModels { get; private set; }

    /// <summary>The AI in use, or null (no AI: everything works as before).</summary>
    public IDialogueWriter? Writer { get; private set; }

    public event Action? Changed;

    public bool Available => Writer is not null;

    public void Load()
    {
        try
        {
            if (File.Exists(_path)) Settings = JsonSerializer.Deserialize(File.ReadAllText(_path), AiJsonContext.Default.AiSettings) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            AppLog.Write("AI settings unreadable: " + ex.Message);
        }
        if (Environment.GetEnvironmentVariable("PETFAM_AI") is "off" or "0") Settings.Provider = AiProvider.Off;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Settings, AiJsonContext.Default.AiSettings));
        }
        catch (IOException ex)
        {
            AppLog.Write("AI settings not saved: " + ex.Message);
        }
    }

    /// <summary>Looks for the CLIs and Ollama, then picks the writer from the settings.</summary>
    public async Task DetectAsync()
    {
        Claude = ClaudeCliWriter.Detect();
        Codex = CodexCliWriter.Detect();
        Gemini = GeminiCliWriter.Detect();
        OllamaModels = await OllamaWriter.ModelsAsync(CancellationToken.None);
        Choose();
    }

    public void Apply(AiSettings settings)
    {
        Settings = settings;
        Save();
        Choose();
    }

    private void Choose()
    {
        var s = Settings;
        IDialogueWriter? Ollama() => OllamaModels is { Count: > 0 } models
            ? new OllamaWriter(s.OllamaModel is { Length: > 0 } m && models.Contains(m) ? m : models[0])
            : null;
        IDialogueWriter? Api() => s.AnthropicKey is { Length: > 10 } key ? new AnthropicApiWriter(key)
            : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { Length: > 10 } envKey ? new AnthropicApiWriter(envKey) : null;

        Writer = s.Provider switch
        {
            AiProvider.Off => null,
            AiProvider.ClaudeCli => Claude,
            AiProvider.CodexCli => Codex,
            AiProvider.GeminiCli => Gemini,
            AiProvider.Ollama => Ollama(),
            AiProvider.AnthropicApi => Api(),
            _ => (IDialogueWriter?)Claude ?? (IDialogueWriter?)Codex ?? (IDialogueWriter?)Gemini ?? Ollama() ?? Api(),
        };
        AppLog.Write($"AI: {(Writer is null ? "none" : Writer.Name)} (setting {s.Provider}; found: " +
                     $"{string.Join(", ", new[] { Claude?.Name, Codex?.Name, Gemini?.Name, OllamaModels is null ? null : "Ollama" }.OfType<string>().DefaultIfEmpty("nothing"))})");
        Changed?.Invoke();
    }

    /// <summary>Asks the AI something with a time limit; null on any failure (logged).</summary>
    public async Task<string?> AskAsync(string system, string prompt, TimeSpan? timeout = null, string? image = null)
    {
        var writer = Writer;
        if (writer is null) return null;
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(90));
        var started = DateTime.UtcNow;
        try
        {
            string? answer;
            if (image is not null)
            {
                if (!writer.CanSee) return null;
                var small = Shrink(image);
                try { answer = await Task.Run(() => writer.SeeAsync(system, prompt, small, cts.Token)); }
                finally { if (small != image) TryDelete(small); }
            }
            else answer = await Task.Run(() => writer.WriteAsync(system, prompt, cts.Token));
            AppLog.Write($"AI: {writer.Name} answered in {(DateTime.UtcNow - started).TotalSeconds:0.0}s");
            return answer;
        }
        catch (Exception ex)
        {
            AppLog.Write($"AI: {writer.Name} failed after {(DateTime.UtcNow - started).TotalSeconds:0.0}s: {ex.GetBaseException().Message}");
            return null;
        }
    }

    /// <summary>A picture no wider than 1280 px (PNG), so it is quick to send. The original when it is small already.</summary>
    public static string Shrink(string image)
    {
        try
        {
            using var bmp = new Bitmap(image);
            if (bmp.PixelSize.Width <= 1280 && new FileInfo(image).Length < 3_000_000) return image;
            var w = Math.Min(1280, bmp.PixelSize.Width);
            var h = Math.Max(1, (int)(bmp.PixelSize.Height * (double)w / bmp.PixelSize.Width));
            using var scaled = bmp.CreateScaledBitmap(new Avalonia.PixelSize(w, h), BitmapInterpolationMode.HighQuality);
            var dir = Path.Combine(Path.GetTempPath(), "PetFam");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"ai-{Guid.NewGuid():N}.png");
            scaled.Save(path);
            return path;
        }
        catch (Exception ex)
        {
            AppLog.Write("AI: could not shrink picture: " + ex.Message);
            return image;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
