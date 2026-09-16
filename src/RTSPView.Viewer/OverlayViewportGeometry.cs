using System.Windows;
using System.Windows.Media;
using RTSPView.Core;

namespace RTSPView.Viewer;

public static class OverlayViewportGeometry
{
    public static Geometry Create(DoorbellOverlaySettings overlay, double width, double height)
    {
        var bounds = new Rect(0, 0, Math.Max(1, width), Math.Max(1, height));
        if (overlay.ViewportShape is DoorbellViewportShape.Circle or DoorbellViewportShape.Oval)
            return new EllipseGeometry(bounds);
        if (overlay.ViewportShape == DoorbellViewportShape.RoundedSquare)
            return new RectangleGeometry(bounds, Math.Min(width, height) * .22, Math.Min(width, height) * .22);
        if (overlay.ViewportShape == DoorbellViewportShape.Custom && CustomViewportPathValidator.IsValid(
            overlay.CustomViewportPathData, overlay.CustomViewportViewBoxX, overlay.CustomViewportViewBoxY,
            overlay.CustomViewportViewBoxWidth, overlay.CustomViewportViewBoxHeight))
        {
            try
            {
                var geometry = PathGeometry.CreateFromGeometry(Geometry.Parse(overlay.CustomViewportPathData));
                geometry.FillRule = FillRule.EvenOdd;
                foreach (var figure in geometry.Figures) figure.IsClosed = true;
                var transforms = new TransformGroup();
                transforms.Children.Add(new TranslateTransform(-overlay.CustomViewportViewBoxX, -overlay.CustomViewportViewBoxY));
                transforms.Children.Add(new ScaleTransform(width / overlay.CustomViewportViewBoxWidth, height / overlay.CustomViewportViewBoxHeight));
                transforms.Children.Add(new RotateTransform(Math.Clamp(overlay.CustomViewportRotationDegrees, -180, 180), width / 2, height / 2));
                geometry.Transform = transforms;
                return geometry;
            }
            catch (Exception error) when (error is FormatException or InvalidOperationException or OverflowException) { }
        }
        return new RectangleGeometry(bounds);
    }
}
