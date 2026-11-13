using System.Text;
using Microsoft.Win32;
using PetFam.Core.Geometry;
using PetFam.Core.Platform;
using PetFam.Platform.Windows.Native;

namespace PetFam.Platform.Windows;

public sealed class WindowsPlatform : IPlatform, IWindowLayering
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    private readonly HashSet<IntPtr> _own = new();

    public event Action<string>? Log;

    // ---------------------------------------------------------------- IPlatform

    public void RegisterOwnWindow(IntPtr handle) { lock (_own) _own.Add(handle); }

    /// <summary>One of this app's windows? (Read from the desktop-poll thread too, hence the lock.)</summary>
    private bool IsOwn(IntPtr h) { lock (_own) return _own.Contains(h); }

    public ForegroundWindowInfo? GetForegroundWindow()
    {
        var h = User32.GetForegroundWindow();
        if (h == IntPtr.Zero || IsOwn(h)) return null;
        if (!User32.IsWindowVisible(h) || User32.IsIconic(h)) return null;

        var cls = GetClassName(h);
        if (ShellClasses.Contains(cls)) return null;

        if (DwmApi.DwmGetWindowAttribute(h, DwmApi.DWMWA_EXTENDED_FRAME_BOUNDS, out var r, System.Runtime.InteropServices.Marshal.SizeOf<User32.RECT>()) != 0)
            if (!User32.GetWindowRect(h, out r)) return null;

        var bounds = RectI.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        if (bounds.Width < 40 || bounds.Height < 40) return null;

        var maximized = User32.IsZoomed(h);
        var fullscreen = CoversMonitor(h, bounds);

        return new ForegroundWindowInfo(h, bounds, GetTitle(h), cls, maximized, fullscreen);
    }

    public Vec2 GetCursorPosition()
    {
        return User32.GetCursorPos(out var p) ? new Vec2(p.X, p.Y) : Vec2.Zero;
    }

    public TimeSpan GetIdleTime()
    {
        var info = new User32.LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<User32.LASTINPUTINFO>() };
        if (!User32.GetLastInputInfo(ref info)) return TimeSpan.Zero;
        var elapsedMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(elapsedMs);
    }

    public bool IsDoNotDisturb()
    {
        if (Shell32.SHQueryUserNotificationState(out var state) == 0)
        {
            if (state is Shell32.QueryUserNotificationState.Busy
                or Shell32.QueryUserNotificationState.RunningD3dFullScreen
                or Shell32.QueryUserNotificationState.PresentationMode)
                return true;
        }

        return GetForegroundWindow()?.IsFullscreen == true;
    }

    public string? GetWallpaperPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var path = key?.GetValue("WallPaper") as string;
            return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sets the desktop wallpaper (Control Panel\Desktop style first, then SPI_SETDESKWALLPAPER so Explorer redraws).</summary>
    public bool SetWallpaper(string path, WallpaperFit fit)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
            {
                key?.SetValue("TileWallpaper", fit == WallpaperFit.Tile ? "1" : "0");
                key?.SetValue("WallpaperStyle", fit switch
                {
                    WallpaperFit.Center or WallpaperFit.Tile => "0",
                    WallpaperFit.Stretch => "2",
                    WallpaperFit.Fit => "6",
                    WallpaperFit.Span => "22",
                    _ => "10",
                });
            }
            var ok = User32.SystemParametersInfo(User32.SPI_SETDESKWALLPAPER, 0, path, User32.SPIF_UPDATEINIFILE | User32.SPIF_SENDWININICHANGE);
            Log?.Invoke($"Wallpaper set to {path} ({fit}): {(ok ? "ok" : "failed " + System.Runtime.InteropServices.Marshal.GetLastWin32Error())}");
            return ok;
        }
        catch (Exception ex)
        {
            Log?.Invoke("Wallpaper not set: " + ex.Message);
            return false;
        }
    }

    public WallpaperFit GetWallpaperFit()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var tile = key?.GetValue("TileWallpaper") as string;
            var style = key?.GetValue("WallpaperStyle") as string;
            if (tile == "1") return WallpaperFit.Tile;
            return style switch
            {
                "0" => WallpaperFit.Center,
                "2" => WallpaperFit.Stretch,
                "6" => WallpaperFit.Fit,
                "22" => WallpaperFit.Span,
                _ => WallpaperFit.Fill,
            };
        }
        catch
        {
            return WallpaperFit.Fill;
        }
    }

    public bool IsDesktopAt(Vec2 p)
    {
        var h = User32.WindowFromPoint(new User32.POINT { X = (int)p.X, Y = (int)p.Y });
        if (h == IntPtr.Zero) return false;
        var root = User32.GetAncestor(h, User32.GA_ROOT);
        if (root == IntPtr.Zero) root = h;
        if (IsOwn(root) || IsOwn(h)) return true;
        var cls = GetClassName(root);
        return cls is "Progman" or "WorkerW";
    }

    // Each thread gets its own IVirtualDesktopManager (COM objects belong to the thread that made them):
    // desktops are read on a worker thread, windows are moved on the UI thread.
    private readonly ThreadLocal<VirtualDesktops> _desktops;
    private VirtualDesktops? _desktopMover;

    public WindowsPlatform() => _desktops = new ThreadLocal<VirtualDesktops>(() => new VirtualDesktops(Log));

    public VirtualDesktopState? GetVirtualDesktops() => _desktops.Value!.Read(User32.GetForegroundWindow(), IsOwn);

    // ---------------------------------------------------------------- IWindowLayering

    public void MoveOwnWindowsToDesktop(string desktopId)
    {
        _desktopMover ??= new VirtualDesktops(Log);
        IntPtr[] own;
        lock (_own) own = _own.ToArray();
        _desktopMover.MoveWindows(own, desktopId);
    }

    public void AttachToDesktopLayer(IntPtr handle, RectI pixelBounds)
    {
        var host = WorkerWHost.Acquire();
        Log?.Invoke($"WorkerW mode: {host.Mode} (0x{host.Handle.ToInt64():X})");

        var ex = User32.GetWindowLongPtr(handle, User32.GWL_EXSTYLE).ToInt64();
        ex &= ~(User32.WS_EX_APPWINDOW | User32.WS_EX_TOPMOST);
        ex |= User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE;
        User32.SetWindowLongPtr(handle, User32.GWL_EXSTYLE, new IntPtr(ex));

        // SetParent does not touch WS_CHILD/WS_POPUP itself; the docs require switching them before the call.
        var style = User32.GetWindowLongPtr(handle, User32.GWL_STYLE).ToInt64();
        style &= ~User32.WS_POPUP;
        style |= User32.WS_CHILD;
        User32.SetWindowLongPtr(handle, User32.GWL_STYLE, new IntPtr(style));

        if (User32.SetParent(handle, host.Handle) == IntPtr.Zero)
            Log?.Invoke($"SetParent failed: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        else
            Log?.Invoke($"Parent now 0x{User32.GetAncestor(handle, User32.GA_PARENT).ToInt64():X}");

        SetDesktopLayerBounds(handle, pixelBounds);
    }

    public void SetDesktopLayerBounds(IntPtr handle, RectI pixelBounds)
    {
        // Child coordinates are relative to the WorkerW client area, whose origin is the virtual screen origin.
        var vx = User32.GetSystemMetrics(User32.SM_XVIRTUALSCREEN);
        var vy = User32.GetSystemMetrics(User32.SM_YVIRTUALSCREEN);
        User32.SetWindowPos(handle, IntPtr.Zero,
            pixelBounds.X - vx, pixelBounds.Y - vy, pixelBounds.Width, pixelBounds.Height,
            User32.SWP_NOZORDER | User32.SWP_NOACTIVATE | User32.SWP_SHOWWINDOW | User32.SWP_FRAMECHANGED);
    }

    public void MakeFrontWindow(IntPtr handle)
    {
        var ex = User32.GetWindowLongPtr(handle, User32.GWL_EXSTYLE).ToInt64();
        ex &= ~(User32.WS_EX_APPWINDOW | User32.WS_EX_TRANSPARENT);
        ex |= User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE | User32.WS_EX_TOPMOST;
        User32.SetWindowLongPtr(handle, User32.GWL_EXSTYLE, new IntPtr(ex));
        User32.SetWindowPos(handle, User32.HWND_TOPMOST, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE | User32.SWP_FRAMECHANGED);
    }

    public string DescribeWindow(IntPtr handle)
    {
        User32.GetWindowRect(handle, out var r);
        var parent = User32.GetAncestor(handle, User32.GA_PARENT);
        return $"rect=[{r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}] parent=0x{parent.ToInt64():X} visible={User32.IsWindowVisible(handle)}";
    }

    public void MakeClickThroughOverlay(IntPtr handle)
    {
        var ex = User32.GetWindowLongPtr(handle, User32.GWL_EXSTYLE).ToInt64();
        ex &= ~User32.WS_EX_APPWINDOW;
        ex |= User32.WS_EX_LAYERED | User32.WS_EX_TRANSPARENT | User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE | User32.WS_EX_TOPMOST;
        User32.SetWindowLongPtr(handle, User32.GWL_EXSTYLE, new IntPtr(ex));
        User32.SetLayeredWindowAttributes(handle, 0, 255, User32.LWA_ALPHA);
        User32.SetWindowPos(handle, User32.HWND_TOPMOST, 0, 0, 0, 0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE | User32.SWP_FRAMECHANGED);
    }

    // ---------------------------------------------------------------- helpers

    private static bool CoversMonitor(IntPtr hwnd, RectI bounds)
    {
        var mon = User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST);
        if (mon == IntPtr.Zero) return false;
        var info = new User32.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<User32.MONITORINFO>() };
        if (!User32.GetMonitorInfo(mon, ref info)) return false;
        var m = RectI.FromLTRB(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
        return bounds.Contains(m);
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        User32.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        User32.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
