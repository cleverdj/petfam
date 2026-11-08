using System.Collections.Concurrent;

namespace PetFam.Desktop;

/// <summary>
/// Tiny file logger so the app can be diagnosed without a debugger. Lines are queued and written by a background
/// thread, so logging never blocks the UI thread on disk I/O (that used to cause visible hitches).
/// </summary>
public static class AppLog
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>());
    private static readonly Thread Writer;

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetFam");

    public static string LogPath { get; } = Path.Combine(DataDirectory, "petfam.log");

    /// <summary>The result of moving the old data folder, logged once the writer runs.</summary>
    private static string? _migrated;

    static AppLog()
    {
        MigrateFromOldName();
        Writer = new Thread(WriteLoop) { IsBackground = true, Name = "PetFam log writer", Priority = ThreadPriority.BelowNormal };
        Writer.Start();
        if (_migrated is not null) Write(_migrated);
    }

    /// <summary>
    /// The app used to be called ZooZoo and kept its data in %LOCALAPPDATA%\ZooZoo. On the first run under the new name
    /// the old folder's files (pets, houses, settings) are copied over; the old folder is left as it was, as a backup.
    /// </summary>
    private static void MigrateFromOldName()
    {
        try
        {
            var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZooZoo");
            if (Directory.Exists(DataDirectory) || !Directory.Exists(old)) return;
            Directory.CreateDirectory(DataDirectory);
            var copied = 0;
            foreach (var file in Directory.GetFiles(old))
            {
                var name = Path.GetFileName(file);
                // The old log stays behind; everything else (save.json, backups, sheets) comes along.
                if (name.Equals("zoozoo.log", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(DataDirectory, name), overwrite: false);
                copied++;
            }
            _migrated = $"Moved to the new name: copied {copied} file(s) from {old} (left in place as a backup)";
        }
        catch (Exception ex)
        {
            _migrated = "Copying data from the old ZooZoo folder failed: " + ex.Message;
        }
    }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        System.Diagnostics.Trace.WriteLine(line);
        try { Queue.Add(line); }
        catch { /* logging must never take the app down */ }
    }

    /// <summary>Waits (briefly) until everything queued is on disk. Called on exit.</summary>
    public static void Flush(int timeoutMs = 1000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (Queue.Count > 0 && Environment.TickCount64 < until) Thread.Sleep(10);
    }

    private static void WriteLoop()
    {
        var batch = new List<string>();
        foreach (var first in Queue.GetConsumingEnumerable())
        {
            batch.Clear();
            batch.Add(first);
            while (batch.Count < 200 && Queue.TryTake(out var more)) batch.Add(more);
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.AppendAllText(LogPath, string.Join(Environment.NewLine, batch) + Environment.NewLine, new System.Text.UTF8Encoding(true));
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
