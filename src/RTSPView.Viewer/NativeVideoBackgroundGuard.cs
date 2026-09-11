using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RTSPView.Viewer;

internal static class NativeVideoBackgroundGuard
{
    private static readonly EnumWindowsCallback Callback = InspectWindow;

    // LibVLCSharp's host is a Win32 STATIC control. Its default WM_PAINT uses
    // WM_CTLCOLORSTATIC from the parent, ignoring the class background brush.
    // Paint the host itself; WS_CLIPCHILDREN keeps VLC's render surface intact.
    // Hook the WPF-owned host only, never the decoder's child window procedure.
    public static nint PaintHostBackground(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x000F) // WM_PAINT
        {
            var dc = BeginPaint(handle, out var paint);
            try { FillBlack(handle, dc); }
            finally { EndPaint(handle, ref paint); }
            handled = true;
        }
        else if (message == 0x0014 || message == 0x0318) // WM_ERASEBKGND / WM_PRINTCLIENT
        {
            FillBlack(handle, wParam);
            handled = true;
            return new nint(1);
        }
        return nint.Zero;
    }

    private static void FillBlack(nint handle, nint dc)
    {
        if (dc != nint.Zero && GetClientRect(handle, out var rectangle))
            FillRect(dc, ref rectangle, GetStockObject(4));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public nint Dc;
        public int Erase;
        public Rectangle Rectangle;
        public int Restore, IncrementalUpdate;
        public int Reserved1, Reserved2, Reserved3, Reserved4, Reserved5, Reserved6, Reserved7, Reserved8;
    }

    [DllImport("user32.dll")] private static extern nint BeginPaint(nint handle, out PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint handle, ref PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out Rectangle rectangle);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref Rectangle rectangle, nint brush);

    public static void Apply(bool forceRedraw = false)
    {
        if (!OperatingSystem.IsWindows()) return;
        EnumWindows(Callback, forceRedraw ? new nint(1) : nint.Zero);
    }

    public static void Apply(nint handle, bool forceRedraw = false)
    {
        if (!OperatingSystem.IsWindows() || handle == nint.Zero) return;
        ApplyBlackBackground(handle, forceRedraw);
    }

    private static bool InspectWindow(nint handle, nint parameter)
    {
        GetWindowThreadProcessId(handle, out var processId);
        if (processId != (uint)Environment.ProcessId) return true;
        ApplyIfVideoWindow(handle, parameter != nint.Zero);
        EnumChildWindows(handle, Callback, parameter);
        return true;
    }

    private static void ApplyIfVideoWindow(nint handle, bool forceRedraw)
    {
        var className = new StringBuilder(256);
        var title = new StringBuilder(256);
        GetClassName(handle, className, className.Capacity);
        GetWindowText(handle, title, title.Capacity);
        var classNameValue = className.ToString();
        var identity = classNameValue + " " + title;
        var style = GetWindowLongPtr(handle, -16).ToInt64();
        var extendedStyle = GetWindowLongPtr(handle, -20).ToInt64();
        var isLibVlcVideoHost = classNameValue.Equals("Static", StringComparison.OrdinalIgnoreCase) &&
            (style & (0x40000000L | 0x02000000L)) == (0x40000000L | 0x02000000L) &&
            (extendedStyle & 0x00000020L) != 0;
        if (!isLibVlcVideoHost && !identity.Contains("VLC", StringComparison.OrdinalIgnoreCase)) return;
        ApplyBlackBackground(handle, forceRedraw);
    }

    private static void ApplyBlackBackground(nint handle, bool forceRedraw)
    {
        var blackBrush = GetStockObject(4);
        var previousBrush = SetClassLongPtr(handle, -10, blackBrush);
        if (forceRedraw || previousBrush != blackBrush)
            RedrawWindow(handle, 0, 0, 0x0001 | 0x0004 | 0x0080);
    }

    private delegate bool EnumWindowsCallback(nint handle, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint handle, StringBuilder className, int maximumCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder title, int maximumCount);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")] private static extern nint SetClassLongPtr(nint handle, int index, nint value);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int objectIndex);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(nint handle, nint updateRectangle, nint updateRegion, uint flags);
}
