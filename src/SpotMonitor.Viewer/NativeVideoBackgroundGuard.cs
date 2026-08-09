using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpotMonitor.Viewer;

internal static class NativeVideoBackgroundGuard
{
    private static readonly HashSet<nint> Configured = [];
    private static readonly EnumWindowsCallback Callback = InspectWindow;

    public static void Apply()
    {
        if (!OperatingSystem.IsWindows()) return;
        EnumWindows(Callback, 0);
    }

    private static bool InspectWindow(nint handle, nint parameter)
    {
        GetWindowThreadProcessId(handle, out var processId);
        if (processId != (uint)Environment.ProcessId) return true;
        ApplyIfVideoWindow(handle);
        EnumChildWindows(handle, Callback, 0);
        return true;
    }

    private static void ApplyIfVideoWindow(nint handle)
    {
        if (Configured.Contains(handle)) return;
        var className = new StringBuilder(256);
        var title = new StringBuilder(256);
        GetClassName(handle, className, className.Capacity);
        GetWindowText(handle, title, title.Capacity);
        var identity = className + " " + title;
        if (!identity.Contains("VLC", StringComparison.OrdinalIgnoreCase)) return;
        SetClassLongPtr(handle, -10, GetStockObject(4));
        Configured.Add(handle);
        RedrawWindow(handle, 0, 0, 0x0001 | 0x0004 | 0x0080);
    }

    private delegate bool EnumWindowsCallback(nint handle, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint handle, StringBuilder className, int maximumCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder title, int maximumCount);
    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")] private static extern nint SetClassLongPtr(nint handle, int index, nint value);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int objectIndex);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(nint handle, nint updateRectangle, nint updateRegion, uint flags);
}
