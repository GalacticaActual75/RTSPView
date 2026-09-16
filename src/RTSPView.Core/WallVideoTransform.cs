namespace RTSPView.Core;

public static class WallVideoTransform
{
    public static DoorbellVideoLayout Calculate(double sourceWidth, double sourceHeight,
        double width, double height, string sizing, double outputTileWidth = 0,
        int zoomPercent = 100, int horizontalPositionPercent = 50, int verticalPositionPercent = 50)
    {
        sourceWidth = Math.Max(1, sourceWidth);
        sourceHeight = Math.Max(1, sourceHeight);
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var scale = sizing == "fill" ? Math.Max(width / sourceWidth, height / sourceHeight)
            : Math.Min(width / sourceWidth, height / sourceHeight);
        if (sizing == "original") scale = outputTileWidth > 0 ? width / outputTileWidth : 1;
        var renderWidth = sizing == "stretch" ? width : sourceWidth * scale;
        var renderHeight = sizing == "stretch" ? height : sourceHeight * scale;
        renderWidth *= Math.Clamp(zoomPercent, 25, 400) / 100d;
        renderHeight *= Math.Clamp(zoomPercent, 25, 400) / 100d;
        return new(renderWidth, renderHeight, (width - renderWidth) * Math.Clamp(horizontalPositionPercent, 0, 100) / 100d,
            (height - renderHeight) * Math.Clamp(verticalPositionPercent, 0, 100) / 100d,
            0, 0, sourceWidth, sourceHeight);
    }
}
