using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using RTSPView.Viewer;

internal static class NativeBackgroundChecks
{
    public static void Run(CameraTile tile)
    {
        var view = (Control)tile.FindName("VideoView");
        var host = (HwndHost)view.Template.FindName("PART_PlayerHost", view);
        var method = typeof(CameraTile)
            .GetMethod("ColorNativeVideoBackground", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var hook = method.CreateDelegate<HwndSourceHook>(tile);
        var parent = HwndSource.FromHwnd(GetParent(host.Handle));
        nint WhiteParent(nint h, int m, nint w, nint l, ref bool handled)
        {
            if (m != 0x0138 || l != host.Handle) return 0; // WM_CTLCOLORSTATIC
            handled = true;
            return GetStockObject(0); // WHITE_BRUSH: reproduce a light parent theme.
        }
        parent.AddHook(WhiteParent);
        try
        {
            // Negative control: the previous class-brush-only fix still paints white.
            parent.RemoveHook(hook);
            try { CheckPixel(host.Handle, 0x0318, 0xFFFFFF, "old host paint reproduces white background"); }
            finally { parent.AddHook(hook); }
            CheckPixel(host.Handle, 0x0318, 0, "native host paints black despite white parent");
            tile.ApplyWallAppearance(new RTSPView.Core.WallLayout { BackgroundColor = "#123456", BorderColor = "#abcdef", ShowTileBorders = false });
            CheckPixel(host.Handle, 0x0318, 0x563412, "native letterboxing paints selected layout background");
            if (((Border)tile.FindName("TileBorder")).BorderThickness != new Thickness(0)) throw new Exception("Layout borderless override failed");
            tile.ApplyWallAppearance(new RTSPView.Core.WallLayout());
            CheckPixel(host.Handle, 0x0318, 0, "native background restores when layout changes");
            CheckPixel(host.Handle, 0x0014, 0xFFFFFF, "native erase leaves existing surface untouched");
            foreach (var message in new[] { 0x000F, 0x0014, 0x0318 })
            {
                var handled = false;
                if (hook(host.Handle, message, 0, host.Handle, ref handled) != 0 || handled)
                    throw new Exception("Background correction intercepted a renderer paint message");
            }
            var otherHandled = false;
            hook(parent.Handle, 0x0138, 0, parent.Handle, ref otherHandled);
            if (otherHandled) throw new Exception("Background correction affected another control");
            Console.WriteLine("PASS renderer paint messages and unrelated controls remain untouched");
        }
        finally { parent.RemoveHook(WhiteParent); }
    }

    private static void CheckPixel(nint handle, int message, uint expected, string label)
    {
        var dc = CreateCompatibleDC(0);
        var bitmap = CreateBitmap(16, 16, 1, 32, 0);
        var previous = SelectObject(dc, bitmap);
        try
        {
            var rectangle = new Rectangle { Right = 16, Bottom = 16 };
            FillRect(dc, ref rectangle, GetStockObject(0));
            SendMessage(handle, message, dc, new nint(4)); // PRF_CLIENT
            var actual = GetPixel(dc, 2, 2);
            if (actual != expected) throw new Exception($"{label}: expected {expected:X6}, got {actual:X8}");
            Console.WriteLine("PASS " + label);
        }
        finally { SelectObject(dc, previous); DeleteObject(bitmap); DeleteDC(dc); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetParent(nint handle);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint handle, int message, nint w, nint l);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref Rectangle rectangle, nint brush);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateBitmap(int width, int height, uint planes, uint bits, nint data);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
}
