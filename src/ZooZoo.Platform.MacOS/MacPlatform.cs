using ZooZoo.Core.Geometry;
using ZooZoo.Core.Platform;

namespace ZooZoo.Platform.MacOS;

/// <summary>
/// macOS implementation placeholder. The app runs on macOS with this stub (pets wander on a normal
/// transparent window), but nothing is layered yet.
///
/// TODO (plan §8.3), implemented through a small Swift dylib + P/Invoke:
///  - Backyard: NSWindow.level = kCGDesktopIconWindowLevel - 1, collectionBehavior = canJoinAllSpaces | stationary | ignoresCycle
///  - Overlay:  NSWindow.level = NSStatusWindowLevel, ignoresMouseEvents = true
///  - Foreground window + caret: AX API (needs Accessibility permission)
///  - Idle: CGEventSource.secondsSinceLastEventType
///  - Wallpaper: NSWorkspace.desktopImageURL(for:)
///  - Spaces: NSWorkspace.activeSpaceDidChangeNotification, CGS private API with version gating
/// </summary>
public sealed class MacPlatform : IPlatform, IWindowLayering
{
    public void RegisterOwnWindow(IntPtr handle) { }

    public ForegroundWindowInfo? GetForegroundWindow() => null;

    public Vec2 GetCursorPosition() => Vec2.Zero;

    public TimeSpan GetIdleTime() => TimeSpan.Zero;

    public bool IsDoNotDisturb() => false;

    public string? GetWallpaperPath() => null;

    public WallpaperFit GetWallpaperFit() => WallpaperFit.Fill;

    public bool IsDesktopAt(Vec2 screenPoint) => true;

    public void MakeFrontWindow(IntPtr handle) { }

    public void AttachToDesktopLayer(IntPtr handle, RectI pixelBounds) { }

    public void SetDesktopLayerBounds(IntPtr handle, RectI pixelBounds) { }

    public void MakeClickThroughOverlay(IntPtr handle) { }

    public string DescribeWindow(IntPtr handle) => "(macOS stub)";
}
