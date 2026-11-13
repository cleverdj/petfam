using PetFam.Platform.Windows.Native;

namespace PetFam.Platform.Windows;

/// <summary>
/// Finds (or makes Explorer create) the WorkerW window that sits between the wallpaper and the desktop
/// icons. Reparenting a window into it puts that window behind every app window and behind the icons.
/// Same technique as Wallpaper Engine / Lively (plan §8.2).
/// </summary>
public static class WorkerWHost
{
    public sealed record Result(IntPtr Handle, string Mode);

    private const uint SpawnWorkerWMessage = 0x052C;

    private static Result? _last;

    /// <summary>
    /// The WorkerW to put backyard windows in. Asked for once and then reused while it exists: asking Progman again
    /// (for a second monitor's window) can make Explorer build a new WorkerW and destroy the old one, taking the
    /// first window with it.
    /// </summary>
    public static Result Acquire()
    {
        if (_last is { } last && User32.IsWindow(last.Handle)) return last;
        return _last = Spawn();
    }

    private static Result Spawn()
    {
        var progman = User32.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
            throw new InvalidOperationException("Progman window not found. Is Explorer running?");

        // Ask Progman to split the wallpaper into a WorkerW behind the icons.
        User32.SendMessageTimeout(progman, SpawnWorkerWMessage, new IntPtr(0xD), IntPtr.Zero, User32.SMTO_NORMAL, 1000, out _);
        User32.SendMessageTimeout(progman, SpawnWorkerWMessage, new IntPtr(0xD), new IntPtr(1), User32.SMTO_NORMAL, 1000, out _);

        // Classic layout (Win7 .. Win11 23H2): a top-level WorkerW right after the one holding SHELLDLL_DefView.
        var workerw = IntPtr.Zero;
        User32.EnumWindows((top, _) =>
        {
            var shell = User32.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shell != IntPtr.Zero)
                workerw = User32.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);

        if (workerw != IntPtr.Zero)
            return new Result(workerw, "workerw-sibling");

        // Windows 11 24H2+: SHELLDLL_DefView lives directly under Progman and the spawned WorkerW is a child of Progman.
        workerw = User32.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (workerw != IntPtr.Zero)
            return new Result(workerw, "workerw-child-of-progman");

        // Last resort: parent to Progman itself. Pets will draw above icons in this mode.
        return new Result(progman, "progman-fallback");
    }
}
