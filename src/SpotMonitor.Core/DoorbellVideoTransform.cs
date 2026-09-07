namespace SpotMonitor.Core;

public readonly record struct DoorbellCropWindow(int X, int Y, int Width, int Height)
{
    public bool IsFullFrame(int sourceWidth, int sourceHeight) =>
        X == 0 && Y == 0 && Width == sourceWidth && Height == sourceHeight;

    public string ToVlcGeometry() => $"{Width}x{Height}+{X}+{Y}";
}

public static class DoorbellVideoTransform
{
    public static DoorbellCropWindow CalculateCropWindow(
        int sourceWidth,
        int sourceHeight,
        int displayWidth,
        int displayHeight,
        DoorbellVideoSizing sizing,
        DoorbellViewportShape shape,
        int zoomPercent,
        int cropLeftPercent,
        int cropRightPercent,
        int cropTopPercent,
        int cropBottomPercent,
        int horizontalPositionPercent,
        int verticalPositionPercent)
    {
        sourceWidth = Math.Max(2, sourceWidth);
        sourceHeight = Math.Max(2, sourceHeight);
        var usableSourceWidth = AlignDown(sourceWidth);
        var usableSourceHeight = AlignDown(sourceHeight);
        var left = AlignNearest(sourceWidth * Math.Clamp(cropLeftPercent, 0, 80) / 100d);
        var right = AlignNearest(sourceWidth * (100 - Math.Clamp(cropRightPercent, 0, 80)) / 100d);
        var top = AlignNearest(sourceHeight * Math.Clamp(cropTopPercent, 0, 80) / 100d);
        var bottom = AlignNearest(sourceHeight * (100 - Math.Clamp(cropBottomPercent, 0, 80)) / 100d);
        left = Math.Clamp(left, 0, usableSourceWidth - 2);
        right = Math.Clamp(right, left + 2, usableSourceWidth);
        top = Math.Clamp(top, 0, usableSourceHeight - 2);
        bottom = Math.Clamp(bottom, top + 2, usableSourceHeight);

        var availableWidth = right - left;
        var availableHeight = bottom - top;
        var cropWidth = availableWidth;
        var cropHeight = availableHeight;
        if (shape != DoorbellViewportShape.Native && sizing == DoorbellVideoSizing.Fit &&
            displayWidth > 0 && displayHeight > 0)
        {
            var sourceAspect = availableWidth / (double)availableHeight;
            var displayAspect = displayWidth / (double)displayHeight;
            if (sourceAspect > displayAspect)
                cropWidth = AlignedSize(availableHeight * displayAspect, availableWidth);
            else if (sourceAspect < displayAspect)
                cropHeight = AlignedSize(availableWidth / displayAspect, availableHeight);
        }

        var zoom = Math.Clamp(zoomPercent, 100, 300) / 100d;
        cropWidth = AlignedSize(cropWidth / zoom, availableWidth);
        cropHeight = AlignedSize(cropHeight / zoom, availableHeight);
        var horizontalSlack = availableWidth - cropWidth;
        var verticalSlack = availableHeight - cropHeight;
        var x = left + AlignedOffset(horizontalSlack, horizontalPositionPercent);
        var y = top + AlignedOffset(verticalSlack, verticalPositionPercent);
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
