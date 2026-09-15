namespace RTSPView.Core;

public static class OverlayGeometry
{
    // The overlay editor uses a 16:9 host canvas. Fit that reference uniformly
    // into the current tile instead of stretching its axes independently.
    public static (double Left, double Top, double Width, double Height) Calculate(
        DoorbellOverlaySettings overlay, double hostWidth, double hostHeight, double? videoAspect = null)
    {
        hostWidth = Math.Max(1, hostWidth);
        hostHeight = Math.Max(1, hostHeight);
        var widthLimit = hostWidth * Math.Clamp(overlay.ViewportWidthPercent, 10, 95) / 100d;
        var heightLimit = hostHeight * Math.Clamp(overlay.ViewportHeightPercent, 10, 95) / 100d;
        var aspect = overlay.ViewportShape is DoorbellViewportShape.Square or DoorbellViewportShape.Circle
            ? 1d
            : 16d / 9 * Math.Clamp(overlay.ViewportWidthPercent, 10, 95) / Math.Clamp(overlay.ViewportHeightPercent, 10, 95);
        var width = Math.Min(widthLimit, heightLimit * aspect);
        var height = width / aspect;
        var imageWidth = hostWidth; var imageHeight = hostHeight;
        if (videoAspect is > 0 && double.IsFinite(videoAspect.Value))
        {
            imageWidth = Math.Min(hostWidth, hostHeight * videoAspect.Value);
            imageHeight = imageWidth / videoAspect.Value;
        }
        // Preserve size, but anchor placement to the picture rather than its bars.
        return (Math.Clamp((hostWidth - imageWidth) / 2 + (imageWidth - width) * Math.Clamp(overlay.ViewportHorizontalPositionPercent, 0, 100) / 100d, 0, hostWidth - width),
            Math.Clamp((hostHeight - imageHeight) / 2 + (imageHeight - height) * Math.Clamp(overlay.ViewportVerticalPositionPercent, 0, 100) / 100d, 0, hostHeight - height),
            width, height);
    }
}
