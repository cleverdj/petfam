using System.Runtime.InteropServices;
using Microsoft.Win32;
using PetFam.Core.Platform;

namespace PetFam.Platform.Windows;

/// <summary>
/// Windows virtual desktops (plan §11 "virtual desktops"). The order, names and per-desktop wallpapers come from the
/// registry Explorer keeps up to date; the current desktop from the registry too, cross-checked with the public
/// IVirtualDesktopManager (the foreground window always sits on the desktop on screen). Our own windows stay on the
/// desktop they were made on, so after a switch they are moved with IVirtualDesktopManager.MoveWindowToDesktop.
/// No private shell interfaces are used.
/// </summary>
internal sealed class VirtualDesktops
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    private readonly IVirtualDesktopManager? _manager;
    private readonly Action<string>? _log;
    private IReadOnlyList<VirtualDesktop> _desktops = Array.Empty<VirtualDesktop>();
    private string _orderKey = "";
    private DateTime _listReadUtc = DateTime.MinValue;

    public VirtualDesktops(Action<string>? log)
    {
        _log = log;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"));
            _manager = type is null ? null : Activator.CreateInstance(type) as IVirtualDesktopManager;
        }
        catch (Exception ex)
        {
            _log?.Invoke("IVirtualDesktopManager unavailable: " + ex.Message);
        }
    }

    /// <summary>Null when the user has never had more than one desktop (Explorer has not written the list yet).</summary>
    public VirtualDesktopState? Read(IntPtr foreground, Func<IntPtr, bool> isOwn)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            if (key?.GetValue("VirtualDesktopIDs") is not byte[] ids || ids.Length < 16) return null;

            // Names and wallpapers: re-read when the order changes, and every few seconds for renames.
            var orderKey = Convert.ToHexString(ids);
            if (orderKey != _orderKey || DateTime.UtcNow - _listReadUtc > TimeSpan.FromSeconds(5))
            {
                _orderKey = orderKey;
                _listReadUtc = DateTime.UtcNow;
                var list = new List<VirtualDesktop>();
                for (var i = 0; i + 16 <= ids.Length; i += 16)
                {
                    var id = new Guid(ids.AsSpan(i, 16));
                    using var d = key.OpenSubKey($@"Desktops\{id:B}");
                    var name = d?.GetValue("Name") as string ?? "";
                    var wall = d?.GetValue("Wallpaper") as string;
                    list.Add(new VirtualDesktop(id.ToString("D"), name, string.IsNullOrWhiteSpace(wall) || !File.Exists(wall) ? null : wall));
                }
                _desktops = list;
            }

            var current = ReadCurrent(key);

            // The foreground window is always on the desktop on screen; trust it over a registry value that lags.
            if (_manager is not null && foreground != IntPtr.Zero && !isOwn(foreground) &&
                _manager.GetWindowDesktopId(foreground, out var fg) == 0 && fg != Guid.Empty)
            {
                var fgId = fg.ToString("D");
                if (_desktops.Any(d => d.Id == fgId)) current = fgId;
            }

            current ??= _desktops[0].Id;
            return new VirtualDesktopState(_desktops, current);
        }
        catch (Exception ex)
        {
            _log?.Invoke("Reading virtual desktops failed: " + ex.Message);
            return null;
        }
    }

    private static string? ReadCurrent(RegistryKey key)
    {
        // Windows 11 keeps it next to the list; Windows 10 under the logon session.
        if (key.GetValue("CurrentVirtualDesktop") is byte[] { Length: 16 } cur) return new Guid(cur).ToString("D");
        if (!ProcessIdToSessionId((uint)Environment.ProcessId, out var session)) return null;
        using var s = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{session}\VirtualDesktops");
        return s?.GetValue("CurrentVirtualDesktop") is byte[] { Length: 16 } c ? new Guid(c).ToString("D") : null;
    }

    /// <summary>Moves our windows to the desktop. Windows already there, or gone, are skipped.</summary>
    public void MoveWindows(IEnumerable<IntPtr> windows, string desktopId)
    {
        if (_manager is null || !Guid.TryParse(desktopId, out var target)) return;
        foreach (var h in windows)
        {
            if (!IsWindow(h)) continue;
            try
            {
                if (_manager.GetWindowDesktopId(h, out var now) != 0 || now == Guid.Empty || now == target) continue;
                var hr = _manager.MoveWindowToDesktop(h, ref target);
                if (hr != 0) _log?.Invoke($"MoveWindowToDesktop 0x{h.ToInt64():X} failed: 0x{hr:X}");
            }
            catch (Exception ex)
            {
                _log?.Invoke("MoveWindowToDesktop failed: " + ex.Message);
            }
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [ComImport]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrentDesktop);
        [PreserveSig] int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }
}
