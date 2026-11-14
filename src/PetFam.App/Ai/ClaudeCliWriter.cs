using System.Diagnostics;
using System.Text;
using PetFam.Core.Talk;

namespace PetFam.Desktop.Ai;

/// <summary>
/// Runs an AI command-line tool the user already has installed and signed in to (their own subscription; no key in
/// PetFam): the prompt goes in on stdin, the answer comes back on stdout. Each run starts from an empty folder.
/// </summary>
public abstract class CliWriter : IDialogueWriter
{
    protected readonly string Exe;
    protected readonly string WorkDir;

    protected CliWriter(string exe)
    {
        Exe = exe;
        WorkDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetFam", "talk");
        Directory.CreateDirectory(WorkDir);
    }

    public abstract string Name { get; }
    public string ExePath => Exe;
    public virtual bool CanSee => false;

    public abstract Task<string?> WriteAsync(string system, string prompt, CancellationToken ct);
    public virtual Task<string?> SeeAsync(string system, string prompt, string imagePath, CancellationToken ct) => Task.FromResult<string?>(null);

    /// <summary>Finds an executable on PATH (and a few usual install folders). On Windows npm tools are .cmd files.</summary>
    public static string? Find(string name)
    {
        var names = OperatingSystem.IsWindows() ? new[] { name + ".exe", name + ".cmd" } : new[] { name };
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        dirs.Add(Path.Combine(home, ".local", "bin"));
        dirs.Add(Path.Combine(home, ".claude", "local"));
        if (OperatingSystem.IsWindows()) dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
        else dirs.AddRange(new[] { "/usr/local/bin", "/opt/homebrew/bin" });
        foreach (var dir in dirs)
        foreach (var n in names)
        {
            try
            {
                var path = Path.Combine(dir.Trim('"'), n);
                if (File.Exists(path)) return path;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }
        return null;
    }

    /// <summary>Runs the tool with these arguments, the input on stdin; returns stdout. Throws on a non-zero exit.</summary>
    protected async Task<string> RunAsync(IEnumerable<string> args, string input, CancellationToken ct)
    {
        ProcessStartInfo psi;
        if (Exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            // A .cmd shim goes through cmd.exe; arguments here are plain words, the prompt itself goes on stdin.
            psi = new ProcessStartInfo("cmd.exe");
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(Exe);
        }
        else psi = new ProcessStartInfo(Exe);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.WorkingDirectory = WorkDir;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        psi.StandardInputEncoding = new UTF8Encoding(false);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"{Name} did not start");
        using var kill = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var text = await output;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Name} exited with {process.ExitCode}: {Trim(await error)} {Trim(text)}");
        return text;
    }

    protected static string Trim(string s) => s.Length <= 200 ? s.Trim() : s[..200].Trim() + "…";
}

/// <summary>
/// Claude Code (<c>claude -p</c>): a small fast model, no tools (only Read, limited to the picture's folder, when
/// looking at a picture), no MCP servers, no settings or project files, no saved session.
/// </summary>
public sealed class ClaudeCliWriter : CliWriter
{
    private const string Model = "haiku";

    private ClaudeCliWriter(string exe) : base(exe) { }

    public override string Name => "Claude";
    public override bool CanSee => true;

    /// <summary>The installed claude CLI, or null. PETFAM_CLAUDE=path picks one.</summary>
    public static ClaudeCliWriter? Detect()
    {
        if (Environment.GetEnvironmentVariable("PETFAM_CLAUDE") is { Length: > 0 } explicitPath)
            return File.Exists(explicitPath) ? new ClaudeCliWriter(explicitPath) : null;
        return Find("claude") is { } path ? new ClaudeCliWriter(path) : null;
    }

    private static List<string> Args(string system, string? tools, string? dir)
    {
        var args = new List<string> { "-p", "--model", Model, "--tools", tools ?? "", "--no-session-persistence", "--strict-mcp-config", "--setting-sources", "" };
        if (tools is not null) { args.Add("--allowedTools"); args.Add(tools); }
        if (dir is not null) { args.Add("--add-dir"); args.Add(dir); }
        args.Add("--system-prompt");
        args.Add(system);
        return args;
    }

    public override Task<string?> WriteAsync(string system, string prompt, CancellationToken ct) =>
        RunAsync(Args(system, null, null), prompt, ct)!;

    public override async Task<string?> SeeAsync(string system, string prompt, string imagePath, CancellationToken ct)
    {
        var full = Path.GetFullPath(imagePath);
        var input = $"Look at the image file {full} with the Read tool.\n\n{prompt}";
        return await RunAsync(Args(system, "Read", Path.GetDirectoryName(full)), input, ct);
    }
}

/// <summary>OpenAI Codex CLI (<c>codex exec</c>, signed in with ChatGPT): read-only sandbox, prompt on stdin.</summary>
public sealed class CodexCliWriter : CliWriter
{
    private CodexCliWriter(string exe) : base(exe) { }

    public override string Name => "Codex";
    public override bool CanSee => true;

    public static CodexCliWriter? Detect() => Find("codex") is { } path ? new CodexCliWriter(path) : null;

    private async Task<string?> Run(string system, string prompt, string? image, CancellationToken ct)
    {
        // The final answer goes to a file: stdout also carries progress output.
        var outFile = Path.Combine(WorkDir, $"codex-{Guid.NewGuid():N}.txt");
        var args = new List<string> { "exec", "--skip-git-repo-check", "--sandbox", "read-only", "--output-last-message", outFile };
        if (image is not null) { args.Add("--image"); args.Add(Path.GetFullPath(image)); }
        args.Add("-");
        try
        {
            var stdout = await RunAsync(args, system + "\n\n" + prompt, ct);
            return File.Exists(outFile) ? await File.ReadAllTextAsync(outFile, ct) : stdout;
        }
        finally
        {
            try { File.Delete(outFile); } catch (IOException) { }
        }
    }

    public override Task<string?> WriteAsync(string system, string prompt, CancellationToken ct) => Run(system, prompt, null, ct);
    public override Task<string?> SeeAsync(string system, string prompt, string imagePath, CancellationToken ct) => Run(system, prompt, imagePath, ct);
}

/// <summary>Google Gemini CLI (signed in with a Google account): non-interactive, prompt on stdin.</summary>
public sealed class GeminiCliWriter : CliWriter
{
    private GeminiCliWriter(string exe) : base(exe) { }

    public override string Name => "Gemini";

    public static GeminiCliWriter? Detect() => Find("gemini") is { } path ? new GeminiCliWriter(path) : null;

    public override async Task<string?> WriteAsync(string system, string prompt, CancellationToken ct) =>
        await RunAsync(new[] { "--prompt", "Answer the request given on standard input." }, system + "\n\n" + prompt, ct);
}
