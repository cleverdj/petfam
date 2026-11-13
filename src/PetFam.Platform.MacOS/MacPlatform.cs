using PetFam.Core.Geometry;
using PetFam.Core.Platform;

namespace PetFam.Platform.MacOS;

/// <summary>
/// macOS platform layer (plan §8.3), through the Objective-C runtime and CoreGraphics directly (no helper dylib):
/// <list type="bullet">
/// <item>Backyard: an NSWindow just above the desktop picture and below the icons, on every Space, ignoring the mouse.</item>
/// <item>Overlay / front windows: status-level windows on every Space (the overlay ignores the mouse).</item>
/// <item>Foreground window: the front-most normal window of the front-most app, from CGWindowList (no Accessibility
/// permission needed: only window rectangles, never contents).</item>
/// <item>Idle time from CGEventSource, the cursor from CGEvent, wallpaper from NSWorkspace.</item>
/// <item>Spaces from the window server (CGSCopyManagedDisplaySpaces), main display's list.</item>
/// </list>
/// Coordinates: PetFam works in screen pixels with the origin at the top-left of the main display, like Avalonia's
/// screens. Quartz gives points with the same origin; they are scaled by the main display's backing scale.
/// Everything is wrapped so a failing call degrades to "nothing known" instead of crashing.
/// </summary>
public sealed class MacPlatform : IPlatform, IWindowLayering
{
    public event Action<string>? Log;

    private readonly int _ownPid = Environment.ProcessId;
    private double _scale = -1;

    /// <summary>Pixels per point on the main display (2 on Retina).</summary>
    private double Scale
    {
        get
        {
            if (_scale > 0) return _scale;
            try
            {
                var d = CG.CGMainDisplayID();
                var points = CG.CGDisplayBounds(d).Size.Width;
                _scale = points > 0 ? Math.Max(1, (double)CG.CGDisplayPixelsWide(d) / points) : 1;
                var screen = ObjC.Class("NSScreen", "mainScreen");
                if (screen != IntPtr.Zero) _scale = Math.Max(_scale, ObjC.SendReturnsDouble(screen, ObjC.Sel("backingScaleFactor")));
            }
            catch { _scale = 1; }
            return _scale;
        }
    }

    private RectI ToPixels(CGRect r) => new((int)(r.Origin.X * Scale), (int)(r.Origin.Y * Scale), (int)(r.Size.Width * Scale), (int)(r.Size.Height * Scale));

    public void RegisterOwnWindow(IntPtr handle) { }   // our windows are recognised by process id

    // ---------------------------------------------------------------- windows on screen

    private sealed record OnScreen(long Pid, long Layer, RectI Bounds, string Owner);

    /// <summary>Normal (layer 0) windows on screen, front to back.</summary>
    private List<OnScreen> Windows()
    {
        var list = new List<OnScreen>();
        var arr = CG.CGWindowListCopyWindowInfo(CG.kCGWindowListOptionOnScreenOnly | CG.kCGWindowListExcludeDesktopElements, 0);
        if (arr == IntPtr.Zero) return list;
        try
        {
            foreach (var w in CF.Items(arr))
            {
                var layer = CF.Long(w, "kCGWindowLayer") ?? -1;
                var pid = CF.Long(w, "kCGWindowOwnerPID") ?? -1;
                var boundsDict = CF.Get(w, "kCGWindowBounds");
                if (boundsDict == IntPtr.Zero || !CG.CGRectMakeWithDictionaryRepresentation(boundsDict, out var rect)) continue;
                list.Add(new OnScreen(pid, layer, ToPixels(rect), CF.String(w, "kCGWindowOwnerName") ?? ""));
            }
        }
        finally { CF.CFRelease(arr); }
        return list;
    }

    public ForegroundWindowInfo? GetForegroundWindow()
    {
        try
        {
            var app = ObjC.Call(ObjC.Class("NSWorkspace", "sharedWorkspace"), "frontmostApplication");
            if (app == IntPtr.Zero) return null;
            var pid = ObjC.SendReturnsInt(app, ObjC.Sel("processIdentifier"));
            if (pid == _ownPid) return null;
            var front = Windows().FirstOrDefault(w => w.Pid == pid && w.Layer == 0 && w.Bounds.Width > 60 && w.Bounds.Height > 40);
            if (front is null) return null;
            var display = ToPixels(CG.CGDisplayBounds(CG.CGMainDisplayID()));
            var full = front.Bounds.Width >= display.Width && front.Bounds.Height >= display.Height;
            return new ForegroundWindowInfo(new IntPtr(pid), front.Bounds, front.Owner, front.Owner, IsMaximized: full, IsFullscreen: full);
        }
        catch { return null; }
    }

    public Vec2 GetCursorPosition()
    {
        try
        {
            var e = CG.CGEventCreate(IntPtr.Zero);
            if (e == IntPtr.Zero) return Vec2.Zero;
            var p = CG.CGEventGetLocation(e);
            CF.CFRelease(e);
            return new Vec2((float)(p.X * Scale), (float)(p.Y * Scale));
        }
        catch { return Vec2.Zero; }
    }

    public TimeSpan GetIdleTime()
    {
        try
        {
            // kCGEventSourceStateCombinedSessionState = 0, kCGAnyInputEventType = ~0.
            var s = CG.CGEventSourceSecondsSinceLastEventType(0, uint.MaxValue);
            return double.IsFinite(s) && s >= 0 ? TimeSpan.FromSeconds(s) : TimeSpan.Zero;
        }
        catch { return TimeSpan.Zero; }
    }

    /// <summary>A full-screen app (presentation, video, game) in front.</summary>
    public bool IsDoNotDisturb() => GetForegroundWindow()?.IsFullscreen ?? false;

    public bool IsDesktopAt(Vec2 p)
    {
        try
        {
            // Bare desktop unless some other app's normal window covers the point.
            return !Windows().Any(w => w.Layer == 0 && w.Pid != _ownPid && w.Bounds.Contains(p));
        }
        catch { return true; }
    }

    // ---------------------------------------------------------------- wallpaper

    public string? GetWallpaperPath()
    {
        try
        {
            var ws = ObjC.Class("NSWorkspace", "sharedWorkspace");
            var screen = ObjC.Class("NSScreen", "mainScreen");
            var url = ObjC.Send(ws, ObjC.Sel("desktopImageURLForScreen:"), screen);
            var path = ObjC.Utf8(ObjC.Call(url, "path"));
            return path is not null && File.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    public WallpaperFit GetWallpaperFit() => WallpaperFit.Fill;

    public bool SetWallpaper(string path, WallpaperFit fit)
    {
        try
        {
            var ws = ObjC.Class("NSWorkspace", "sharedWorkspace");
            var url = ObjC.SendString(ObjC.GetClass("NSURL"), ObjC.Sel("fileURLWithPath:"), path);
            var options = ObjC.Class("NSDictionary", "dictionary");
            var screens = ObjC.Class("NSScreen", "screens");
            var ok = true;
            var count = (long)ObjC.Call(screens, "count");
            for (long i = 0; i < count; i++)
            {
                var screen = ObjC.Send(screens, ObjC.Sel("objectAtIndex:"), new IntPtr(i));
                ok &= ObjC.SendReturnsBool(ws, ObjC.Sel("setDesktopImageURL:forScreen:options:error:"), url, screen, options, IntPtr.Zero);
            }
            Log?.Invoke($"Wallpaper set to {path}: {(ok ? "ok" : "failed")}");
            return ok;
        }
        catch (Exception ex)
        {
            Log?.Invoke("Wallpaper not set: " + ex.Message);
            return false;
        }
    }

    // ---------------------------------------------------------------- Spaces

    /// <summary>The main display's Spaces in order and the one on screen (ids are the window server's space ids).</summary>
    public VirtualDesktopState? GetVirtualDesktops()
    {
        try
        {
            var cid = CG.CGSMainConnectionID();
            var displays = CG.CGSCopyManagedDisplaySpaces(cid);
            if (displays == IntPtr.Zero) return null;
            try
            {
                var active = CG.CGSGetActiveSpace(cid).ToString();
                foreach (var display in CF.Items(displays))
                {
                    var spaces = CF.Items(CF.Get(display, "Spaces"))
                        .Select(s => (Id: (CF.Long(s, "ManagedSpaceID") ?? CF.Long(s, "id64") ?? 0).ToString(), Type: CF.Long(s, "type") ?? 0))
                        .Where(s => s.Type == 0 && s.Id != "0")          // 0 = a normal desktop (full-screen apps are 4)
                        .Select(s => new VirtualDesktop(s.Id, "", null))
                        .ToList();
                    if (spaces.Count == 0) continue;
                    var current = spaces.Any(s => s.Id == active) ? active : spaces[0].Id;
                    return new VirtualDesktopState(spaces, current);
                }
            }
            finally { CF.CFRelease(displays); }
        }
        catch (Exception ex) { Log?.Invoke("Spaces not readable: " + ex.Message); }
        return null;
    }

    // ---------------------------------------------------------------- our windows

    // NSWindowCollectionBehavior
    private const ulong CanJoinAllSpaces = 1 << 0;
    private const ulong Stationary = 1 << 4;
    private const ulong IgnoresCycle = 1 << 6;
    private const ulong FullScreenAuxiliary = 1 << 8;
    private const long StatusWindowLevel = 25;

    /// <summary>The NSWindow for a handle Avalonia gives us (an NSView or the window itself).</summary>
    private static IntPtr WindowOf(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return IntPtr.Zero;
        var nsWindow = ObjC.GetClass("NSWindow");
        if (ObjC.SendReturnsBool(handle, ObjC.Sel("isKindOfClass:"), nsWindow)) return handle;
        return ObjC.Call(handle, "window");
    }

    private void Configure(IntPtr handle, long level, ulong behavior, bool ignoreMouse, string what)
    {
        try
        {
            var w = WindowOf(handle);
            if (w == IntPtr.Zero) { Log?.Invoke($"{what}: no NSWindow"); return; }
            ObjC.SendLong(w, ObjC.Sel("setLevel:"), level);
            ObjC.SendULong(w, ObjC.Sel("setCollectionBehavior:"), behavior);
            ObjC.SendBool(w, ObjC.Sel("setIgnoresMouseEvents:"), ignoreMouse);
            ObjC.SendBool(w, ObjC.Sel("setHasShadow:"), false);
            ObjC.SendBool(w, ObjC.Sel("setOpaque:"), false);
            Log?.Invoke($"{what}: level {level}");
        }
        catch (Exception ex) { Log?.Invoke($"{what} failed: {ex.Message}"); }
    }

    public void AttachToDesktopLayer(IntPtr handle, RectI pixelBounds)
    {
        // Just above the desktop picture: below the icons and every app window, on every Space.
        Configure(handle, CG.CGWindowLevelForKey(CG.kCGDesktopWindowLevelKey) + 1, CanJoinAllSpaces | Stationary | IgnoresCycle, ignoreMouse: true, "Backyard");
        SetDesktopLayerBounds(handle, pixelBounds);
    }

    public void SetDesktopLayerBounds(IntPtr handle, RectI b)
    {
        try
        {
            var w = WindowOf(handle);
            if (w == IntPtr.Zero) return;
            // Cocoa frames are in points with the origin at the bottom-left of the main display.
            var main = CG.CGDisplayBounds(CG.CGMainDisplayID());
            var s = Scale;
            var frame = new CGRect(b.X / s, main.Size.Height - (b.Y + b.Height) / s, b.Width / s, b.Height / s);
            ObjC.SendRect(w, ObjC.Sel("setFrame:display:"), frame, true);
        }
        catch (Exception ex) { Log?.Invoke("Backyard bounds failed: " + ex.Message); }
    }

    public void MakeClickThroughOverlay(IntPtr handle) =>
        Configure(handle, StatusWindowLevel, CanJoinAllSpaces | Stationary | IgnoresCycle | FullScreenAuxiliary, ignoreMouse: true, "Overlay");

    public void MakeFrontWindow(IntPtr handle) =>
        Configure(handle, StatusWindowLevel, CanJoinAllSpaces | IgnoresCycle | FullScreenAuxiliary, ignoreMouse: false, "Front window");

    /// <summary>Our windows are on every Space already (canJoinAllSpaces): nothing to move.</summary>
    public void MoveOwnWindowsToDesktop(string desktopId) { }

    public string DescribeWindow(IntPtr handle)
    {
        try
        {
            var w = WindowOf(handle);
            return w == IntPtr.Zero ? "(no NSWindow)" : $"NSWindow 0x{w.ToInt64():X} level={ObjC.SendReturnsLong(w, ObjC.Sel("level"))} scale={Scale:F1}";
        }
        catch (Exception ex) { return "(" + ex.Message + ")"; }
    }
}
