using System.Runtime.InteropServices;

namespace PetFam.Platform.MacOS;

[StructLayout(LayoutKind.Sequential)]
internal struct CGPoint
{
    public double X, Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGSize
{
    public double Width, Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGRect
{
    public CGPoint Origin;
    public CGSize Size;

    public CGRect(double x, double y, double w, double h)
    {
        Origin = new CGPoint { X = x, Y = y };
        Size = new CGSize { Width = w, Height = h };
    }
}

/// <summary>Objective-C runtime: classes, selectors and message sends (one overload per signature we use).</summary>
internal static class ObjC
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";

    [DllImport(Lib, EntryPoint = "objc_getClass")]
    public static extern IntPtr GetClass(string name);

    [DllImport(Lib, EntryPoint = "sel_registerName")]
    public static extern IntPtr Sel(string name);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, IntPtr c, IntPtr d);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendString(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string a);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendLong(IntPtr receiver, IntPtr selector, long a);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendULong(IntPtr receiver, IntPtr selector, ulong a);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendBool(IntPtr receiver, IntPtr selector, bool a);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendReturnsBool(IntPtr receiver, IntPtr selector, IntPtr a);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendReturnsBool(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, IntPtr c, IntPtr d);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern long SendReturnsLong(IntPtr receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern int SendReturnsInt(IntPtr receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern double SendReturnsDouble(IntPtr receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendRect(IntPtr receiver, IntPtr selector, CGRect rect, bool display);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendReturnsUtf8(IntPtr receiver, IntPtr selector);

    public static IntPtr Call(IntPtr receiver, string selector) => Send(receiver, Sel(selector));

    public static IntPtr Class(string name, string selector) => Send(GetClass(name), Sel(selector));

    public static string? Utf8(IntPtr nsString) =>
        nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(SendReturnsUtf8(nsString, Sel("UTF8String")));
}

/// <summary>CoreFoundation: just enough to read CGWindowList and Spaces dictionaries.</summary>
internal static class CF
{
    private const string Lib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int kCFStringEncodingUTF8 = 0x08000100;
    private const int kCFNumberSInt64Type = 4;
    private const int kCFNumberDoubleType = 13;

    [DllImport(Lib)] public static extern void CFRelease(IntPtr obj);
    [DllImport(Lib)] public static extern long CFArrayGetCount(IntPtr array);
    [DllImport(Lib)] public static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
    [DllImport(Lib)] public static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
    [DllImport(Lib)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, [MarshalAs(UnmanagedType.LPUTF8Str)] string s, int encoding);
    [DllImport(Lib)] private static extern bool CFNumberGetValue(IntPtr number, int type, out long value);
    [DllImport(Lib, EntryPoint = "CFNumberGetValue")] private static extern bool CFNumberGetDouble(IntPtr number, int type, out double value);
    [DllImport(Lib)] private static extern long CFStringGetLength(IntPtr s);
    [DllImport(Lib)] private static extern bool CFStringGetCString(IntPtr s, byte[] buffer, long size, int encoding);
    [DllImport(Lib)] private static extern ulong CFGetTypeID(IntPtr obj);
    [DllImport(Lib)] private static extern ulong CFStringGetTypeID();
    [DllImport(Lib)] private static extern ulong CFNumberGetTypeID();

    private static readonly Dictionary<string, IntPtr> Keys = new();

    /// <summary>A CFString key (kept for the life of the app).</summary>
    public static IntPtr Key(string name)
    {
        lock (Keys)
        {
            if (!Keys.TryGetValue(name, out var k)) Keys[name] = k = CFStringCreateWithCString(IntPtr.Zero, name, kCFStringEncodingUTF8);
            return k;
        }
    }

    public static IntPtr Get(IntPtr dict, string key) => dict == IntPtr.Zero ? IntPtr.Zero : CFDictionaryGetValue(dict, Key(key));

    public static long? Long(IntPtr dict, string key)
    {
        var v = Get(dict, key);
        if (v == IntPtr.Zero || CFGetTypeID(v) != CFNumberGetTypeID()) return null;
        return CFNumberGetValue(v, kCFNumberSInt64Type, out long l) ? l : null;
    }

    public static double? Double(IntPtr dict, string key)
    {
        var v = Get(dict, key);
        if (v == IntPtr.Zero || CFGetTypeID(v) != CFNumberGetTypeID()) return null;
        return CFNumberGetDouble(v, kCFNumberDoubleType, out double d) ? d : null;
    }

    public static string? String(IntPtr dict, string key)
    {
        var v = Get(dict, key);
        if (v == IntPtr.Zero || CFGetTypeID(v) != CFStringGetTypeID()) return null;
        var buf = new byte[CFStringGetLength(v) * 4 + 1];
        return CFStringGetCString(v, buf, buf.Length, kCFStringEncodingUTF8) ? System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0') : null;
    }

    public static IEnumerable<IntPtr> Items(IntPtr array)
    {
        if (array == IntPtr.Zero) yield break;
        var n = CFArrayGetCount(array);
        for (long i = 0; i < n; i++) yield return CFArrayGetValueAtIndex(array, i);
    }
}

/// <summary>CoreGraphics (and the window-server calls behind Spaces, which CoreGraphics exports as CGS*).</summary>
internal static class CG
{
    private const string Lib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    public const uint kCGWindowListOptionOnScreenOnly = 1;
    public const uint kCGWindowListExcludeDesktopElements = 16;
    public const int kCGDesktopWindowLevelKey = 2;

    [DllImport(Lib)] public static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(Lib)] public static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out CGRect rect);
    [DllImport(Lib)] public static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(Lib)] public static extern CGPoint CGEventGetLocation(IntPtr evt);
    [DllImport(Lib)] public static extern double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);
    [DllImport(Lib)] public static extern uint CGMainDisplayID();
    [DllImport(Lib)] public static extern CGRect CGDisplayBounds(uint display);
    [DllImport(Lib)] public static extern nuint CGDisplayPixelsWide(uint display);
    [DllImport(Lib)] public static extern int CGWindowLevelForKey(int key);

    // Spaces: private but long-stable window-server calls (used by many window managers).
    [DllImport(Lib)] public static extern int CGSMainConnectionID();
    [DllImport(Lib)] public static extern IntPtr CGSCopyManagedDisplaySpaces(int connection);
    [DllImport(Lib)] public static extern ulong CGSGetActiveSpace(int connection);
}
