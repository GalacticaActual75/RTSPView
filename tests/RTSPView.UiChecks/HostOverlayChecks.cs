using System.Reflection;
using System.Windows;
using System.Windows.Media;
using RTSPView.Core;
using RTSPView.Viewer;

internal static class HostOverlayChecks
{
    public static void Run()
    {
        var host = new CameraTile();
        var overlayTile = new CameraTile();
        var boundsMethod = typeof(MainWindow).GetMethod("CalculateOverlayBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
        var overlay = new DoorbellOverlaySettings { ViewportShape = DoorbellViewportShape.Custom,
            ViewportWidthPercent = 40, ViewportHeightPercent = 40,
            CustomViewportPathData = "M0 0 L1 0 L0.8 1 L0 1 Z", CustomViewportRotationDegrees = 12 };
        try
        {
            foreach (var sizing in new[] { "fit", "fill", "stretch", "original" })
            foreach (var size in new[] { new Size(680, 382.5), new Size(510, 310), new Size(400, 700) })
            {
                host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
                host.SetWallSizing(new WallTile { Sizing = sizing, ZoomPercent = 135, HorizontalPositionPercent = 30, VerticalPositionPercent = 70 }, 680);
                var image = host.GetHostImageLayout();
                var expected = OverlayGeometry.FollowImage(overlay, 1600, 900, image);
                var actual = (Rect)boundsMethod.Invoke(null, [host, overlay])!;
                if (Math.Abs(actual.Bottom - image.OffsetY - image.RenderHeight) > .001)
                    throw new Exception("Bottom-aligned mask left a gap at the actual camera picture edge");
                overlayTile.ApplyVideoSizing(100, 50, 50, actual.Width, actual.Height, expected.ReferenceWidth, expected.ReferenceHeight);
                overlayTile.ApplyViewportEdgeSmoothing(overlay, actual.Width, actual.Height, expected.ReferenceWidth, expected.ReferenceHeight);
                var brush = (DrawingBrush)overlayTile.OpacityMask;
                var geometry = ((GeometryDrawing)((DrawingGroup)brush.Drawing).Children[1]).Geometry;
                var reference = OverlayViewportGeometry.Create(overlay, expected.ReferenceWidth, expected.ReferenceHeight);
                foreach (var point in new[] { new Point(.1, .1), new Point(.5, .5), new Point(.85, .8) })
                {
                    var p = new Point(point.X * expected.ReferenceWidth, point.Y * expected.ReferenceHeight);
                    var projected = new Point(point.X * actual.Width, point.Y * actual.Height);
                    if (reference.FillContains(p) != geometry.FillContains(projected))
                        throw new Exception("Rotated custom mask changed source alignment when host stretched");
                }
            }
            Console.WriteLine("PASS actual host tile framing, custom overlay placement, bottom-edge alignment and rotated mask projection across layouts.");
        }
        finally { host.Dispose(); overlayTile.Dispose(); }
    }
}
