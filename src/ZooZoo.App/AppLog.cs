namespace ZooZoo.Desktop;

/// <summary>Tiny file logger so the first spike runs can be diagnosed without a debugger.</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZooZoo");

    public static string LogPath { get; } = Path.Combine(DataDirectory, "zoozoo.log");

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        System.Diagnostics.Trace.WriteLine(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DataDirectory);
                File.AppendAllText(LogPath, line + Environment.NewLine, new System.Text.UTF8Encoding(true));
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
