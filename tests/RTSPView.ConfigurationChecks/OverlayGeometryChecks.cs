using RTSPView.Core;

internal static class OverlayGeometryChecks
{
    public static void Run()
    {
        foreach (var shape in Enum.GetValues<DoorbellViewportShape>())
        foreach (var position in new[] { 0, 50, 100 })
        {
            var overlay = new DoorbellOverlaySettings { ViewportShape = shape,
                ViewportWidthPercent = 60, ViewportHeightPercent = 40,
                ViewportHorizontalPositionPercent = position, ViewportVerticalPositionPercent = position,
                CustomViewportPathData = "M0 0 L1000 0 L500 1000 Z", ZoomPercent = 135 };
            var reference = OverlayGeometry.Calculate(overlay, 1600, 900);
            foreach (var host in new[] { (1600d, 900d), (900d, 1600d), (400d, 400d), (200d, 900d), (2400d, 250d) })
            {
                var result = OverlayGeometry.Calculate(overlay, host.Item1, host.Item2);
                Check(Math.Abs(result.Width / result.Height - reference.Width / reference.Height) < 1e-9, "Layout stretched overlay shape");
                Check(result.Width <= host.Item1 * .6 + 1e-9 && result.Height <= host.Item2 * .4 + 1e-9, "Overlay exceeds configured bounds");
                Check(result.Left >= 0 && result.Top >= 0 && result.Left + result.Width <= host.Item1 + 1e-9 && result.Top + result.Height <= host.Item2 + 1e-9, "Overlay escaped host");
                Check(Math.Abs(result.Left - (host.Item1 - result.Width) * position / 100d) < 1e-9, "Horizontal anchor moved");
                Check(Math.Abs(result.Top - (host.Item2 - result.Height) * position / 100d) < 1e-9, "Vertical anchor moved");
            }
            Check(OverlayGeometry.Calculate(overlay, 1600, 900) == reference, "Restoring layout drifted");
            Check(overlay.ZoomPercent == 135 && overlay.ViewportWidthPercent == 60, "Runtime sizing edited saved settings");
        }
        var anchored = new DoorbellOverlaySettings { ViewportWidthPercent=40, ViewportHeightPercent=40, ViewportVerticalPositionPercent=100, ViewportHorizontalPositionPercent=0 };
        var oldBounds=OverlayGeometry.Calculate(anchored,400,400);
        var pictureBounds=OverlayGeometry.Calculate(anchored,400,400,16d/9);
        Check(oldBounds.Width==pictureBounds.Width && oldBounds.Height==pictureBounds.Height,"Picture anchoring resized overlay");
        Check(Math.Abs(pictureBounds.Top+pictureBounds.Height-312.5)<1e-9,"Overlay did not anchor to picture bottom above letterbox bar");
        var side=OverlayGeometry.Calculate(anchored,800,400,4d/3);
        Check(Math.Abs(side.Left-(800-400*4d/3)/2)<1e-9,"Overlay did not anchor past pillarbox bar");
        Check(OverlayGeometry.Calculate(anchored,1600,900,16d/9)==OverlayGeometry.Calculate(anchored,1600,900),"Matching aspect placement changed");
        Console.WriteLine("PASS overlay uniform scaling, picture anchors without resizing, all shapes, portrait/landscape/narrow hosts and restoration");
        var mask = anchored with { ViewportShape = DoorbellViewportShape.Custom };
        var baseline = OverlayGeometry.Calculate(mask, 1600, 900);
        foreach (var sizing in new[] { "fit", "fill", "stretch", "original" })
        foreach (var host in new[] { (1600d, 900d), (510d, 310d), (400d, 700d) })
        foreach (var zoom in new[] { 100, 175 })
        {
            var image = WallVideoTransform.Calculate(1600, 900, host.Item1, host.Item2, sizing, 800, zoom, 25, 75);
            var mapped = OverlayGeometry.FollowImage(mask, 1600, 900, image);
            Check(Math.Abs((mapped.Left - image.OffsetX) / image.RenderWidth - baseline.Left / 1600) < 1e-9, "Mask horizontal source anchor drifted");
            Check(Math.Abs((mapped.Top - image.OffsetY) / image.RenderHeight - baseline.Top / 900) < 1e-9, "Mask vertical source anchor drifted");
            Check(Math.Abs(mapped.Width / image.RenderWidth - baseline.Width / 1600) < 1e-9 && Math.Abs(mapped.Height / image.RenderHeight - baseline.Height / 900) < 1e-9, "Mask did not follow host distortion");
            Check(mapped.ReferenceWidth == baseline.Width && mapped.ReferenceHeight == baseline.Height, "Overlay video crop changed with wall layout");
        }
        Console.WriteLine("PASS source-anchored custom masks across fit/fill/stretch/original, odd layouts, zoom and pan; stable reference crop.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
