using System.Windows;

namespace RTSPView.Viewer;

internal static class OverlayWindowOpacity
{
    public static void Apply(Window window, int percent)
    {
        // Let WPF coordinate its HwndTarget and layered-window state. Directly
        // setting WS_EX_LAYERED behind WPF's back can be undone by WM_STYLECHANGING
        // and makes SetLayeredWindowAttributes fail with ERROR_INVALID_PARAMETER.
        window.Opacity = Math.Clamp(percent, 20, 100) / 100d;
        foreach (Window foreground in window.OwnedWindows)
            foreground.Opacity = window.Opacity;
    }
}
