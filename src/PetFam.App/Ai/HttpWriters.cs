using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PetFam.Core.Talk;

namespace PetFam.Desktop.Ai;

/// <summary>A local model in Ollama (http://localhost:11434). Nothing leaves the computer.</summary>
public sealed class OllamaWriter : IDialogueWriter
{
    public const string BaseUrl = "http://127.0.0.1:11434";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private readonly string _model;

    public OllamaWriter(string model) => _model = model;

    public string Name => $"Ollama ({_model})";
    public bool CanSee => true;   // vision models (llava, llama3.2-vision, gemma3…) look at it; others ignore the picture

    /// <summary>Ollama running here, and its models (first is the default), or null.</summary>
    public static async Task<IReadOnlyList<string>?> ModelsAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var json = await Http.GetStringAsync(BaseUrl + "/api/tags", cts.Token);
            var models = JsonNode.Parse(json)?["models"]?.AsArray().Select(m => m?["name"]?.GetValue<string>()).OfType<string>().ToList();
            return models is { Count: > 0 } ? models : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private async Task<string?> Chat(string system, string prompt, string? image, CancellationToken ct)
    {
        var user = new JsonObject { ["role"] = "user", ["content"] = prompt };
        if (image is not null) user["images"] = new JsonArray(Convert.ToBase64String(await File.ReadAllBytesAsync(image, ct)));
        var body = new JsonObject
        {
            ["model"] = _model,
            ["stream"] = false,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system }, user),
        };
        using var res = await Http.PostAsync(BaseUrl + "/api/chat", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama {(int)res.StatusCode}: {text}");
        return JsonNode.Parse(text)?["message"]?["content"]?.GetValue<string>();
    }

    public Task<string?> WriteAsync(string system, string prompt, CancellationToken ct) => Chat(system, prompt, null, ct);
    public Task<string?> SeeAsync(string system, string prompt, string imagePath, CancellationToken ct) => Chat(system, prompt, imagePath, ct);
}

/// <summary>The Anthropic API with the user's own key (billed to that key, separately from any subscription).</summary>
public sealed class AnthropicApiWriter : IDialogueWriter
{
    public const string Model = "claude-haiku-5-5";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly string _key;

    public AnthropicApiWriter(string key) => _key = key;

    public string Name => "Claude API";
    public bool CanSee => true;

    private async Task<string?> Send(string system, JsonArray content, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = 1024,
            ["system"] = system,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content }),
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", _key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        using var res = await Http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Claude API {(int)res.StatusCode}: {(text.Length > 200 ? text[..200] : text)}");
        var blocks = JsonNode.Parse(text)?["content"]?.AsArray();
        return blocks is null ? null : string.Concat(blocks.Where(b => b?["type"]?.GetValue<string>() == "text").Select(b => b!["text"]!.GetValue<string>()));
    }

    public Task<string?> WriteAsync(string system, string prompt, CancellationToken ct) =>
        Send(system, new JsonArray(new JsonObject { ["type"] = "text", ["text"] = prompt }), ct);

    public async Task<string?> SeeAsync(string system, string prompt, string imagePath, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(imagePath, ct);
        var media = Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };
        return await Send(system, new JsonArray(
            new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = media, ["data"] = Convert.ToBase64String(bytes) },
            },
            new JsonObject { ["type"] = "text", ["text"] = prompt }), ct);
    }
}
