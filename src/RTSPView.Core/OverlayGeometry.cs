namespace RTSPView.Core;

public static class OverlayGeometry
{
    // Custom masks are calibrated over the editor's 16:9 camera picture.
    // Project the entire mask and its contents through the host's actual framing.
    public static (double Left, double Top, double Width, double Height, double ReferenceWidth, double ReferenceHeight) FollowImage(
        DoorbellOverlaySettings overlay, double sourceWidth, double sourceHeight, DoorbellVideoLayout image)
    {
        var reference = Calculate(overlay, 1600, 900);
        var canvas = WallVideoTransform.Calculate(sourceWidth, sourceHeight, 1600, 900, "fill");
        var scaleX = image.RenderWidth / canvas.RenderWidth;
        var scaleY = image.RenderHeight / canvas.RenderHeight;
        return (image.OffsetX + (reference.Left - canvas.OffsetX) * scaleX,
            image.OffsetY + (reference.Top - canvas.OffsetY) * scaleY,
            reference.Width * scaleX, reference.Height * scaleY, reference.Width, reference.Height);
    }
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
