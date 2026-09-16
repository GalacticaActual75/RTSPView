using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using RTSPView.Core;

namespace RTSPView.Viewer;

internal static class NativeVideoSurfaceLayout
{
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoCopyBits = 0x0100;
    private const uint SwpAsyncWindowPos = 0x4000;

    public static bool Apply(
        HwndHost videoHost,
        int sourceWidth,
        int sourceHeight,
        int zoomPercent,
        int horizontalPositionPercent,
        int verticalPositionPercent,
        string? wallSizing = null, double outputTileWidth = 0)
    {
        if (!OperatingSystem.IsWindows() || videoHost.Handle == IntPtr.Zero ||
            sourceWidth <= 0 || sourceHeight <= 0)
            return false;

        var videoOutputWindow = FindVideoOutputWindow(videoHost.Handle);
        if (videoOutputWindow == IntPtr.Zero ||
            !GetClientRect(videoHost.Handle, out var viewportRectangle))
            return false;

        var viewportWidth = Math.Max(1, viewportRectangle.Right - viewportRectangle.Left);
        var viewportHeight = Math.Max(1, viewportRectangle.Bottom - viewportRectangle.Top);
        var layout = wallSizing is not null ? WallVideoTransform.Calculate(sourceWidth, sourceHeight,
            viewportWidth, viewportHeight, wallSizing, outputTileWidth, zoomPercent, horizontalPositionPercent, verticalPositionPercent) : DoorbellVideoTransform.CalculateLayout(
            sourceWidth, sourceHeight, viewportWidth, viewportHeight,
            zoomPercent, horizontalPositionPercent, verticalPositionPercent);

        var x = (int)Math.Round(layout.OffsetX);
        var y = (int)Math.Round(layout.OffsetY);
        var width = Math.Max(1, (int)Math.Ceiling(layout.RenderWidth));
        var height = Math.Max(1, (int)Math.Ceiling(layout.RenderHeight));
        if (GetWindowRect(videoOutputWindow, out var current))
        {
            var currentOrigin = new NativePoint { X = current.Left, Y = current.Top };
            MapWindowPoints(IntPtr.Zero, videoHost.Handle, ref currentOrigin, 1);
            if (currentOrigin.X == x && currentOrigin.Y == y &&
                current.Right - current.Left == width && current.Bottom - current.Top == height)
                return true;
        }

        return SetWindowPos(videoOutputWindow, IntPtr.Zero, x, y, width, height,
            SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder | SwpNoCopyBits | SwpAsyncWindowPos);
    }

    private static IntPtr FindVideoOutputWindow(IntPtr videoHost)
    {
        // LibVLC 3 creates a "VLC video main ..." window directly under the
        // caller-provided HWND, then its render window beneath that. WPF owns
        // the provided HwndHost bounds and will undo attempts to move it, so
        // framing must be applied to LibVLC's direct child instead.
        var result = IntPtr.Zero;
        EnumChildWindows(videoHost, (candidate, _) =>
        {
            if (GetParent(candidate) != videoHost) return true;
            var className = new StringBuilder(128);
            GetClassName(candidate, className, className.Capacity);
            if (!className.ToString().StartsWith("VLC video main ", StringComparison.OrdinalIgnoreCase))
                return true;
            result = candidate;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr window);

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref NativePoint points, uint pointCount);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
