using System.Runtime.InteropServices;
using System.Windows.Media;
using RTSPView.Core;

namespace RTSPView.Viewer;

internal static class CustomViewportRegion
{
    private const int AlternateFillMode = 1;

    public static IntPtr Create(DoorbellOverlaySettings overlay, int pixelWidth, int pixelHeight)
    {
        if (!CustomViewportPathValidator.IsValid(
                overlay.CustomViewportPathData,
                overlay.CustomViewportViewBoxX,
                overlay.CustomViewportViewBoxY,
                overlay.CustomViewportViewBoxWidth,
                overlay.CustomViewportViewBoxHeight))
            return IntPtr.Zero;

        PathGeometry geometry;
        try
        {
            var sourceUnitsPerPixel = Math.Max(
                overlay.CustomViewportViewBoxWidth / Math.Max(1, pixelWidth),
                overlay.CustomViewportViewBoxHeight / Math.Max(1, pixelHeight));
            geometry = Geometry.Parse(overlay.CustomViewportPathData).GetFlattenedPathGeometry(
                Math.Clamp(sourceUnitsPerPixel * 0.4, 0.01, 1000),
                ToleranceType.Absolute);
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or OverflowException)
        {
            return IntPtr.Zero;
        }

        var points = new List<NativePoint>();
        var polygonPointCounts = new List<int>();
        foreach (var figure in geometry.Figures)
        {
            var polygon = new List<System.Windows.Point> { figure.StartPoint };
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        polygon.Add(line.Point);
                        break;
                    case PolyLineSegment polyLine:
                        polygon.AddRange(polyLine.Points);
                        break;
                }
            }

            var nativePolygon = polygon
                .Select(point => ToNativePoint(point, overlay, pixelWidth, pixelHeight))
                .Where((point, index) => index == 0 || point != ToNativePoint(polygon[index - 1], overlay, pixelWidth, pixelHeight))
                .ToList();
            if (nativePolygon.Count > 1 && nativePolygon[0] == nativePolygon[^1])
                nativePolygon.RemoveAt(nativePolygon.Count - 1);
            if (nativePolygon.Count < 3) continue;
            points.AddRange(nativePolygon);
            polygonPointCounts.Add(nativePolygon.Count);
        }

        return points.Count >= 3 && polygonPointCounts.Count > 0
            ? CreatePolyPolygonRgn(points.ToArray(), polygonPointCounts.ToArray(), polygonPointCounts.Count, AlternateFillMode)
            : IntPtr.Zero;
    }

    private static NativePoint ToNativePoint(
        System.Windows.Point point,
        DoorbellOverlaySettings overlay,
        int pixelWidth,
        int pixelHeight)
    {
        var x = (point.X - overlay.CustomViewportViewBoxX) /
            overlay.CustomViewportViewBoxWidth * (pixelWidth + 1d);
        var y = (point.Y - overlay.CustomViewportViewBoxY) /
            overlay.CustomViewportViewBoxHeight * (pixelHeight + 1d);
        var radians = Math.Clamp(overlay.CustomViewportRotationDegrees, -180, 180) * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var centerX = (pixelWidth + 1d) / 2d;
        var centerY = (pixelHeight + 1d) / 2d;
        var rotatedX = centerX + (x - centerX) * cosine - (y - centerY) * sine;
        var rotatedY = centerY + (x - centerX) * sine + (y - centerY) * cosine;
        return new NativePoint(
            Math.Clamp((int)Math.Round(rotatedX), 0, pixelWidth + 1),
            Math.Clamp((int)Math.Round(rotatedY), 0, pixelHeight + 1));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreatePolyPolygonRgn(
        [In] NativePoint[] points,
        [In] int[] polygonPointCounts,
        int polygonCount,
        int fillMode);
}
