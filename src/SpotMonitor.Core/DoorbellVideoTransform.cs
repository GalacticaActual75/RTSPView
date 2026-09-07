namespace SpotMonitor.Core;

public readonly record struct DoorbellCropWindow(int X, int Y, int Width, int Height)
{
    public bool IsFullFrame(int sourceWidth, int sourceHeight) =>
        X == 0 && Y == 0 && Width == sourceWidth && Height == sourceHeight;

    public string ToVlcGeometry() => $"{Width}x{Height}+{X}+{Y}";
}

public readonly record struct DoorbellVideoLayout(
    double RenderWidth,
    double RenderHeight,
    double OffsetX,
    double OffsetY,
    double SourceX,
    double SourceY,
    double SourceWidth,
    double SourceHeight);

public static class DoorbellVideoTransform
{
    public static DoorbellVideoLayout CalculateLayout(
        double sourceWidth,
        double sourceHeight,
        double displayWidth,
        double displayHeight,
        int zoomPercent,
        int horizontalPositionPercent,
        int verticalPositionPercent)
    {
        sourceWidth = Math.Max(1, sourceWidth);
        sourceHeight = Math.Max(1, sourceHeight);
        displayWidth = Math.Max(1, displayWidth);
        displayHeight = Math.Max(1, displayHeight);
        var zoom = Math.Clamp(zoomPercent, 100, 300) / 100d;
        var scale = Math.Max(displayWidth / sourceWidth, displayHeight / sourceHeight) * zoom;
        var renderWidth = sourceWidth * scale;
        var renderHeight = sourceHeight * scale;
        var horizontalPosition = Math.Clamp(horizontalPositionPercent, 0, 100) / 100d;
        var verticalPosition = Math.Clamp(verticalPositionPercent, 0, 100) / 100d;
        var offsetX = -(renderWidth - displayWidth) * horizontalPosition;
        var offsetY = -(renderHeight - displayHeight) * verticalPosition;

        return new DoorbellVideoLayout(
            renderWidth,
            renderHeight,
            offsetX,
            offsetY,
            -offsetX / scale,
            -offsetY / scale,
            displayWidth / scale,
            displayHeight / scale);
    }

    public static DoorbellCropWindow CalculateCropWindow(
        int sourceWidth,
        int sourceHeight,
        int displayWidth,
        int displayHeight,
        int zoomPercent,
        int horizontalPositionPercent,
        int verticalPositionPercent)
    {
        sourceWidth = Math.Max(2, sourceWidth);
        sourceHeight = Math.Max(2, sourceHeight);
        var layout = CalculateLayout(
            sourceWidth, sourceHeight, displayWidth, displayHeight,
            zoomPercent, horizontalPositionPercent, verticalPositionPercent);
        var cropWidth = AlignedSize(layout.SourceWidth, sourceWidth);
        var cropHeight = AlignedSize(layout.SourceHeight, sourceHeight);
        var x = AlignedOffset(sourceWidth - cropWidth, horizontalPositionPercent);
        var y = AlignedOffset(sourceHeight - cropHeight, verticalPositionPercent);
        return new DoorbellCropWindow(x, y, cropWidth, cropHeight);
    }

    private static int AlignedSize(double requested, int maximum)
    {
        if (maximum <= 2) return maximum;
        return Math.Clamp(AlignNearest(requested), 2, AlignDown(maximum));
    }

    private static int AlignedOffset(int slack, int positionPercent)
    {
        if (slack <= 0) return 0;
        return Math.Clamp(AlignNearest(slack * Math.Clamp(positionPercent, 0, 100) / 100d), 0, AlignDown(slack));
    }

    private static int AlignNearest(double value) => Math.Max(0, (int)Math.Round(value / 2d) * 2);
    private static int AlignDown(int value) => Math.Max(2, value - value % 2);
}
