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
        int zoomPercent,
        int horizontalPositionPercent,
        int verticalPositionPercent)
    {
        sourceWidth = Math.Max(2, sourceWidth);
        sourceHeight = Math.Max(2, sourceHeight);
        var usableSourceWidth = AlignDown(sourceWidth);
        var usableSourceHeight = AlignDown(sourceHeight);
        var cropWidth = usableSourceWidth;
        var cropHeight = usableSourceHeight;
        if (displayWidth > 0 && displayHeight > 0)
        {
            var sourceAspect = usableSourceWidth / (double)usableSourceHeight;
            var displayAspect = displayWidth / (double)displayHeight;
            if (sourceAspect > displayAspect)
                cropWidth = AlignedSize(usableSourceHeight * displayAspect, usableSourceWidth);
            else if (sourceAspect < displayAspect)
                cropHeight = AlignedSize(usableSourceWidth / displayAspect, usableSourceHeight);
        }

        var zoom = Math.Clamp(zoomPercent, 100, 300) / 100d;
        cropWidth = AlignedSize(cropWidth / zoom, usableSourceWidth);
        cropHeight = AlignedSize(cropHeight / zoom, usableSourceHeight);
        var horizontalSlack = usableSourceWidth - cropWidth;
        var verticalSlack = usableSourceHeight - cropHeight;
        var x = AlignedOffset(horizontalSlack, horizontalPositionPercent);
        var y = AlignedOffset(verticalSlack, verticalPositionPercent);
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
