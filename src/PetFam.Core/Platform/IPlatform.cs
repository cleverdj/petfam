using PetFam.Core.Geometry;

namespace PetFam.Core.Platform;

public sealed record ForegroundWindowInfo(
    IntPtr Handle,
    RectI Bounds,
    string Title,
    string ClassName,
    bool IsMaximized,
    bool IsFullscreen);

/// <summary>One virtual desktop: id, the name the user gave it, and its own wallpaper if it has one.</summary>
public sealed record VirtualDesktop(string Id, string Name, string? WallpaperPath);

/// <summary>The virtual desktops in OS order and the one on screen.</summary>
public sealed record VirtualDesktopState(IReadOnlyList<VirtualDesktop> Desktops, string CurrentId);

public enum WallpaperFit
{
    Fill,
    Fit,
    Stretch,
    Center,
    Tile,
    Span,
}

/// <summary>
/// Everything the core needs from the OS. Implemented per platform (Windows: Win32/COM, macOS: AppKit/AX).
/// The core never reads screen content: only window rects, caret rects, cursor and idle time (plan §11).
/// </summary>
public interface IPlatform
{
    /// <summary>Windows owned by this app, so they are never treated as the user's foreground window.</summary>
    void RegisterOwnWindow(IntPtr handle);

    /// <summary>The user's current foreground window, or null if it is the desktop/shell/our own window.</summary>
    ForegroundWindowInfo? GetForegroundWindow();

    Vec2 GetCursorPosition();

    TimeSpan GetIdleTime();

    /// <summary>True when the OS reports a fullscreen app, presentation, or similar busy state (auto do-not-disturb).</summary>
    bool IsDoNotDisturb();

    string? GetWallpaperPath();

    WallpaperFit GetWallpaperFit();

    /// <summary>True when the window under the point is the bare desktop (or one of our own windows): a pet there is visible and touchable.</summary>
    bool IsDesktopAt(Vec2 screenPoint);

    /// <summary>
    /// Virtual desktops in OS order and the current one. Null when the OS has none or they cannot be read
    /// (then everything happens on one desktop).
    /// </summary>
    VirtualDesktopState? GetVirtualDesktops() => null;
}

/// <summary>Window layering operations that need a native window handle.</summary>
public interface IWindowLayering
{
    /// <summary>
    /// Put the window on the desktop layer: above the wallpaper, behind desktop icons and every app window.
    /// Bounds are in screen pixels.
    /// </summary>
    void AttachToDesktopLayer(IntPtr handle, RectI pixelBounds);

    /// <summary>Force the native pixel bounds of a desktop-layer window (the UI toolkit may resize it after a DPI change).</summary>
    void SetDesktopLayerBounds(IntPtr handle, RectI pixelBounds);

    /// <summary>Make the window a topmost, non-activating, click-through overlay.</summary>
    void MakeClickThroughOverlay(IntPtr handle);

    /// <summary>Make the window a topmost, non-activating tool window that DOES receive mouse input (front yard interaction).</summary>
    void MakeFrontWindow(IntPtr handle);

    /// <summary>Moves this app's windows to the given virtual desktop (they stay on the one they were made on otherwise).</summary>
    void MoveOwnWindowsToDesktop(string desktopId) { }

    /// <summary>Native rect/parent/dpi of a window, for logs.</summary>
    string DescribeWindow(IntPtr handle);
}
