using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RTSPView.Core;
using RTSPView.Viewer;

internal static class OverlayEdgeChecks
{
    public static void Run()
    {
        using var tile = new CameraTile();
        ((FrameworkElement)tile.FindName("VideoView")).Visibility = Visibility.Collapsed;
        foreach (var shape in new[] { DoorbellViewportShape.Circle, DoorbellViewportShape.RoundedSquare, DoorbellViewportShape.Custom })
        {
            var settings = new DoorbellOverlaySettings { ViewportShape = shape, ShowBorder = false,
                CustomViewportPathData = "M10 20 L110 20 L60 120 Z", CustomViewportViewBoxX = 10, CustomViewportViewBoxY = 20,
                CustomViewportViewBoxWidth = 100, CustomViewportViewBoxHeight = 100, CustomViewportRotationDegrees = 15 };
            tile.ApplyViewportEdgeSmoothing(settings, 127, 93);
            tile.Measure(new Size(127, 93)); tile.Arrange(new Rect(0, 0, 127, 93)); tile.UpdateLayout();
            var bitmap = new RenderTargetBitmap(127, 93, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(tile);
            var pixels = new byte[127 * 93 * 4]; bitmap.CopyPixels(pixels, 127 * 4, 0);
            var alpha = Enumerable.Range(0, pixels.Length / 4).Select(i => pixels[i * 4 + 3]).ToArray();
            if (!alpha.Any(a => a > 0 && a < 255) || !alpha.Contains((byte)0) || !alpha.Contains((byte)255))
                throw new Exception($"{shape}: expected transparent exterior, opaque interior and antialiased edge pixels.");
            if (((FrameworkElement)tile.FindName("ViewportBorder")).Visibility != Visibility.Collapsed)
                throw new Exception("Overlay border toggle did not hide the border.");
            tile.ApplyViewportEdgeSmoothing(settings with { ShowBorder = true }, 127, 93);
            if (((FrameworkElement)tile.FindName("ViewportBorder")).Visibility != Visibility.Visible)
                throw new Exception("Overlay border toggle did not restore the border.");
        }
        tile.ApplyTileBorder(false);
        if (((Border)tile.FindName("TileBorder")).BorderThickness != new Thickness(0)) throw new Exception("Gapless tile retained a border.");
        tile.ApplyTileBorder(true);
        if (((Border)tile.FindName("TileBorder")).BorderThickness != new Thickness(1)) throw new Exception("Tile border did not restore.");
        Console.WriteLine("Overlay edge checks passed: circle, rounded and rotated custom alpha edges; independent border toggles.");
    }
}
