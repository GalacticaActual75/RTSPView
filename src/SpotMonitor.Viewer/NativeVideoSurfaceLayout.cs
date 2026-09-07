using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SpotMonitor.Core;

namespace SpotMonitor.Viewer;

internal static class NativeVideoSurfaceLayout
{
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    public static void Apply(
        HwndHost videoHost,
        FrameworkElement viewport,
        int sourceWidth,
        int sourceHeight,
        int zoomPercent,
        int horizontalPositionPercent,
        int verticalPositionPercent)
    {
        if (!OperatingSystem.IsWindows() || videoHost.Handle == IntPtr.Zero ||
            sourceWidth <= 0 || sourceHeight <= 0 ||
            viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0)
            return;

        var parent = GetParent(videoHost.Handle);
        if (parent == IntPtr.Zero) return;

        System.Windows.Point screenOrigin;
        try { screenOrigin = viewport.PointToScreen(new System.Windows.Point(0, 0)); }
        catch (InvalidOperationException) { return; }

        var dpi = VisualTreeHelper.GetDpi(viewport);
        var viewportWidth = Math.Max(1, (int)Math.Round(viewport.ActualWidth * dpi.DpiScaleX));
        var viewportHeight = Math.Max(1, (int)Math.Round(viewport.ActualHeight * dpi.DpiScaleY));
        var layout = DoorbellVideoTransform.CalculateLayout(
            sourceWidth, sourceHeight, viewportWidth, viewportHeight,
            zoomPercent, horizontalPositionPercent, verticalPositionPercent);
        var parentOrigin = new NativePoint
        {
            X = (int)Math.Round(screenOrigin.X),
            Y = (int)Math.Round(screenOrigin.Y)
        };
        MapWindowPoints(IntPtr.Zero, parent, ref parentOrigin, 1);

        var x = parentOrigin.X + (int)Math.Round(layout.OffsetX);
        var y = parentOrigin.Y + (int)Math.Round(layout.OffsetY);
        var width = Math.Max(1, (int)Math.Ceiling(layout.RenderWidth));
        var height = Math.Max(1, (int)Math.Ceiling(layout.RenderHeight));
        if (GetWindowRect(videoHost.Handle, out var current))
        {
            var currentOrigin = new NativePoint { X = current.Left, Y = current.Top };
            MapWindowPoints(IntPtr.Zero, parent, ref currentOrigin, 1);
            if (currentOrigin.X == x && currentOrigin.Y == y &&
                current.Right - current.Left == width && current.Bottom - current.Top == height)
                return;
        }

        SetWindowPos(videoHost.Handle, IntPtr.Zero, x, y, width, height,
            SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
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

    [DllImport("user32.dll")]
    private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref NativePoint points, uint pointCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
