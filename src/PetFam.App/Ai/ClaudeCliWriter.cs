using System.Diagnostics;
using System.Text;
using PetFam.Core.Talk;

namespace PetFam.Desktop.Ai;

/// <summary>
/// Writes pet dialogue with the Claude Code CLI the user already has installed and signed in to (their own
/// subscription; no API key in PetFam). Runs <c>claude -p</c> with no tools, no MCP servers, no settings or
/// project files and no saved session, from an empty folder, with a small fast model. Only the pets' prompt is sent.
/// </summary>
public sealed class ClaudeCliWriter : IDialogueWriter
{
    private const string Model = "haiku";

    private readonly string _exe;
    private readonly string _workDir;

    private ClaudeCliWriter(string exe)
    {
        _exe = exe;
        _workDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetFam", "talk");
        Directory.CreateDirectory(_workDir);
    }

    public string Name => "Claude";

    public string ExePath => _exe;

    /// <summary>
    /// The installed claude CLI, or null. PETFAM_CLAUDE=path picks one; PETFAM_AI=off turns AI talk off.
    /// </summary>
    public static ClaudeCliWriter? Detect()
    {
        if (Environment.GetEnvironmentVariable("PETFAM_AI") is "off" or "0") return null;
        if (Environment.GetEnvironmentVariable("PETFAM_CLAUDE") is { Length: > 0 } explicitPath)
            return File.Exists(explicitPath) ? new ClaudeCliWriter(explicitPath) : null;

        var names = OperatingSystem.IsWindows() ? new[] { "claude.exe" } : new[] { "claude" };
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        dirs.Add(Path.Combine(home, ".local", "bin"));
        dirs.Add(Path.Combine(home, ".claude", "local"));
        if (!OperatingSystem.IsWindows()) dirs.AddRange(new[] { "/usr/local/bin", "/opt/homebrew/bin" });
        foreach (var dir in dirs)
        foreach (var name in names)
        {
            try
            {
                var path = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(path)) return new ClaudeCliWriter(path);
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }
        return null;
    }

    public async Task<string?> WriteAsync(string system, string prompt, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_exe)
        {
            WorkingDirectory = _workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var arg in new[]
                 {
                     "-p", "--model", Model, "--tools", "", "--no-session-persistence", "--strict-mcp-config",
                     "--setting-sources", "", "--system-prompt", system,
                 })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("claude did not start");
        using var kill = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var text = await output;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"claude exited with {process.ExitCode}: {Trim(await error)} {Trim(text)}");
        return text;
    }

    private static string Trim(string s) => s.Length <= 200 ? s.Trim() : s[..200].Trim() + "…";
}
