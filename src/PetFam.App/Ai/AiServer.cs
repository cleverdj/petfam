using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PetFam.Desktop.Ai;

/// <summary>One tool PetFam offers to AI apps over MCP.</summary>
public sealed record McpTool(string Name, string Description, JsonObject InputSchema, Func<JsonObject, Task<string>> Run);

/// <summary>
/// A tiny HTTP server on 127.0.0.1 only, for two things:
/// <list type="bullet">
/// <item><c>POST /mcp</c>: an MCP server (Streamable HTTP, JSON responses) so the user's AI app (Claude Code, Claude
/// Desktop through mcp-remote, …) can ask how the pets are, feed them, make them do things and read their diaries.</item>
/// <item><c>GET|POST /agent?event=working|done|waiting|failed</c>: the user's coding agent (Claude Code hooks) tells
/// the pets what it is up to, and they cheer it on.</item>
/// </list>
/// Requests from web pages are refused (an Origin header, or a Host that is not loopback), so a site in the browser
/// can't drive the pets.
/// </summary>
public sealed class AiServer : IDisposable
{
    public const int DefaultPort = 47800;
    public const string ProtocolVersion = "2025-06-18";

    private readonly int _port;
    private readonly IReadOnlyList<McpTool> _tools;
    private readonly Action<string> _onAgentEvent;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public AiServer(int port, IReadOnlyList<McpTool> tools, Action<string> onAgentEvent)
    {
        _port = port;
        _tools = tools;
        _onAgentEvent = onAgentEvent;
    }

    public int Port => _port;
    public string McpUrl => $"http://127.0.0.1:{_port}/mcp";
    public string AgentUrl => $"http://127.0.0.1:{_port}/agent";
    public bool Running => _listener is not null;

    public bool Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
        }
        catch (SocketException ex)
        {
            AppLog.Write($"AI server: port {_port} unavailable: {ex.Message}");
            _listener = null;
            return false;
        }
        _cts = new CancellationTokenSource();
        _ = AcceptLoop(_cts.Token);
        AppLog.Write($"AI server: listening on 127.0.0.1:{_port} (MCP {McpUrl}, agent events {AgentUrl})");
        return true;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch (SocketException) { }
        _listener = null;
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { } l)
        {
            TcpClient client;
            try { client = await l.AcceptTcpClientAsync(ct); }
            catch (Exception) { break; }
            _ = Task.Run(() => Serve(client, ct), ct);
        }
    }

    private async Task Serve(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = 5000;
            var stream = client.GetStream();
            var (method, path, headers, body) = await ReadRequest(stream, ct);
            if (method is null) return;
            int status;
            string? json = null;

            // No web pages: browsers always send Origin on cross-site POSTs, and a rebinding attack has a foreign Host.
            var host = headers.GetValueOrDefault("host") ?? "";
            if (headers.ContainsKey("origin") || !(host.StartsWith("127.0.0.1") || host.StartsWith("localhost")))
                status = 403;
            else if (path.StartsWith("/agent"))
            {
                var ev = Query(path, "event") ?? TryJson(body)?["event"]?.GetValue<string>();
                if (ev is { Length: > 0 and < 40 }) Avalonia.Threading.Dispatcher.UIThread.Post(() => _onAgentEvent(ev.ToLowerInvariant()));
                status = 204;                                   // no body: hooks may read stdout
            }
            else if (path.StartsWith("/mcp") && method == "POST")
            {
                var reply = await Mcp(body);
                if (reply is null) status = 202;
                else { status = 200; json = reply.ToJsonString(); }
            }
            else if (path.StartsWith("/mcp")) status = 405;
            else if (path == "/" || path.StartsWith("/health")) { status = 200; json = "{\"app\":\"PetFam\"}"; }
            else status = 404;

            var bytes = json is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(json);
            var head = $"HTTP/1.1 {status} {Reason(status)}\r\nContent-Length: {bytes.Length}\r\n" +
                       (json is null ? "" : "Content-Type: application/json\r\n") + "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
            if (bytes.Length > 0) await stream.WriteAsync(bytes, ct);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            // The client went away.
        }
        catch (Exception ex)
        {
            AppLog.Write("AI server: " + ex.Message);
        }
    }

    private static async Task<(string? Method, string Path, Dictionary<string, string> Headers, string Body)> ReadRequest(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new List<byte>();
        var chunk = new byte[4096];
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            var n = await stream.ReadAsync(chunk, ct);
            if (n == 0) return (null, "", new(), "");
            buffer.AddRange(chunk.AsSpan(0, n).ToArray());
            headerEnd = IndexOf(buffer, "\r\n\r\n"u8.ToArray());
            if (buffer.Count > 64 * 1024) return (null, "", new(), "");
        }
        var headText = Encoding.ASCII.GetString(buffer.GetRange(0, headerEnd).ToArray());
        var lines = headText.Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2) return (null, "", new(), "");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
        }
        var length = headers.TryGetValue("content-length", out var cl) && int.TryParse(cl, out var len) ? Math.Clamp(len, 0, 1_000_000) : 0;
        var body = buffer.Skip(headerEnd + 4).ToList();
        while (body.Count < length)
        {
            var n = await stream.ReadAsync(chunk, ct);
            if (n == 0) break;
            body.AddRange(chunk.AsSpan(0, n).ToArray());
        }
        return (first[0].ToUpperInvariant(), first[1], headers, Encoding.UTF8.GetString(body.ToArray()));
    }

    private static int IndexOf(List<byte> data, byte[] pattern)
    {
        for (var i = 0; i + pattern.Length <= data.Count; i++)
        {
            var ok = true;
            for (var j = 0; j < pattern.Length && ok; j++) ok = data[i + j] == pattern[j];
            if (ok) return i;
        }
        return -1;
    }

    private static string? Query(string path, string key)
    {
        var q = path.IndexOf('?');
        if (q < 0) return null;
        foreach (var part in path[(q + 1)..].Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part[..eq] == key) return Uri.UnescapeDataString(part[(eq + 1)..]);
        }
        return null;
    }

    private static JsonNode? TryJson(string text)
    {
        try { return text.Length == 0 ? null : JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK", 202 => "Accepted", 204 => "No Content", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Error",
    };

    // ---------------------------------------------------------------- MCP (JSON-RPC 2.0)

    /// <summary>Answers one JSON-RPC message (or a batch); null for notifications only.</summary>
    public async Task<JsonNode?> Mcp(string body)
    {
        var node = TryJson(body);
        if (node is JsonArray batch)
        {
            var replies = new JsonArray();
            foreach (var m in batch)
                if (m is JsonObject o && await One(o) is { } r) replies.Add(r);
            return replies.Count == 0 ? null : replies;
        }
        if (node is JsonObject single) return await One(single);
        return Error(null, -32700, "Parse error");
    }

    private async Task<JsonObject?> One(JsonObject msg)
    {
        var id = msg["id"]?.DeepClone();
        var method = msg["method"]?.GetValue<string>();
        if (id is null) return null;                           // a notification (initialized, cancelled…)
        var p = msg["params"] as JsonObject ?? new JsonObject();
        switch (method)
        {
            case "initialize":
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = p["protocolVersion"]?.GetValue<string>() ?? ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JsonObject { ["name"] = "petfam", ["title"] = "PetFam", ["version"] = "1.0" },
                    ["instructions"] = "Tools for the user's desktop pets in PetFam: see how they are, feed them, ask them to do things, make them say something, call them over and read their diaries.",
                });
            case "ping":
                return Result(id, new JsonObject());
            case "tools/list":
                return Result(id, new JsonObject
                {
                    ["tools"] = new JsonArray(_tools.Select(t => (JsonNode)new JsonObject
                    {
                        ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = t.InputSchema.DeepClone(),
                    }).ToArray()),
                });
            case "tools/call":
                var name = p["name"]?.GetValue<string>();
                var tool = _tools.FirstOrDefault(t => t.Name == name);
                if (tool is null) return Error(id, -32602, $"Unknown tool: {name}");
                string text;
                var isError = false;
                try { text = await tool.Run(p["arguments"] as JsonObject ?? new JsonObject()); }
                catch (Exception ex) { text = ex.Message; isError = true; }
                AppLog.Write($"MCP: {name} -> {(text.Length > 80 ? text[..80] + "…" : text).ReplaceLineEndings(" ")}");
                return Result(id, new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                    ["isError"] = isError,
                });
            default:
                return Error(id, -32601, $"Method not found: {method}");
        }
    }

    private static JsonObject Result(JsonNode id, JsonObject result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
