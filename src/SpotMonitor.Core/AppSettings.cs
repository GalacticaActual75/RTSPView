namespace SpotMonitor.Core;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 10;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    // Retained for automatic migration from the Phase 1 settings file.
    public CameraSettings Camera { get; init; } = new();
    public IReadOnlyList<CameraSettings> Cameras { get; init; } = CreateCameraSlots();
    public DoorbellOverlaySettings DoorbellOverlay { get; init; } = new();
    public bool RequestHardwareDecoding { get; init; } = true;
    public bool StartFullScreen { get; init; } = true;
    public int PreferredMonitor { get; init; }
    public bool HideMouseCursor { get; init; } = true;
    public int MouseCursorHideSeconds { get; init; } = 3;
    public bool ShowCameraNames { get; init; } = true;
    public bool ShowCameraStats { get; init; } = true;
    public bool KeepViewerAlwaysOnTop { get; init; } = true;

    public static IReadOnlyList<CameraSettings> CreateCameraSlots() =>
        Enumerable.Range(1, 9).Select(slot => new CameraSettings { Slot = slot, Name = $"Camera {slot}" }).ToArray();

    public static CameraSettings CreateDoorbellCamera() => new()
    {
        Slot = 10,
        Name = "Doorbell",
        Enabled = false
    };

    public AppSettings Normalize()
    {
        var normalized = CreateCameraSlots().ToArray();
        foreach (var camera in Cameras.Take(9))
        {
            var index = camera.Slot is >= 1 and <= 9 ? camera.Slot - 1 : Array.IndexOf(Cameras.ToArray(), camera);
            if (index is >= 0 and < 9) normalized[index] = camera with { Slot = index + 1 };
        }
        if (normalized.All(camera => string.IsNullOrWhiteSpace(camera.RtspUrl)) && !string.IsNullOrWhiteSpace(Camera.RtspUrl))
            normalized[0] = Camera with { Slot = 1 };
        for (var index = 0; index < normalized.Length; index++)
            normalized[index] = NormalizeCamera(normalized[index], index + 1, $"Camera {index + 1}");

        var overlay = DoorbellOverlay ?? new DoorbellOverlaySettings();
        var position = Enum.IsDefined(overlay.Position) ? overlay.Position : PictureInPicturePosition.BottomLeft;
        var videoSizing = Enum.IsDefined(overlay.VideoSizing) ? overlay.VideoSizing : DoorbellVideoSizing.Fit;
        var viewportShape = Enum.IsDefined(overlay.ViewportShape) ? overlay.ViewportShape : DoorbellViewportShape.Native;
        var viewportWidthPercent = SchemaVersion < 9 ? overlay.SizePercent : overlay.ViewportWidthPercent;
        var viewportHeightPercent = SchemaVersion < 9 ? overlay.SizePercent : overlay.ViewportHeightPercent;
        var (cropLeftPercent, cropRightPercent) = NormalizeCropPair(overlay.CropLeftPercent, overlay.CropRightPercent);
        var (cropTopPercent, cropBottomPercent) = NormalizeCropPair(overlay.CropTopPercent, overlay.CropBottomPercent);
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Cameras = normalized,
            DoorbellOverlay = overlay with
            {
                HostCameraSlot = Math.Clamp(overlay.HostCameraSlot, 1, 9),
                Position = position,
                SizePercent = Math.Clamp(overlay.SizePercent, 25, 90),
                ViewportWidthPercent = Math.Clamp(viewportWidthPercent, 10, 95),
                ViewportHeightPercent = Math.Clamp(viewportHeightPercent, 10, 95),
                VideoSizing = videoSizing,
                ViewportShape = viewportShape,
                HorizontalOffsetPercent = Math.Clamp(overlay.HorizontalOffsetPercent, -50, 50),
                VerticalOffsetPercent = Math.Clamp(overlay.VerticalOffsetPercent, -50, 50),
                ZoomPercent = Math.Clamp(overlay.ZoomPercent, 100, 300),
                CropLeftPercent = cropLeftPercent,
                CropRightPercent = cropRightPercent,
                CropTopPercent = cropTopPercent,
                CropBottomPercent = cropBottomPercent,
                ImageHorizontalPositionPercent = Math.Clamp(overlay.ImageHorizontalPositionPercent, 0, 100),
                ImageVerticalPositionPercent = Math.Clamp(overlay.ImageVerticalPositionPercent, 0, 100),
                Camera = NormalizeCamera(overlay.Camera ?? CreateDoorbellCamera(), 10, "Doorbell")
            },
            StartFullScreen = SchemaVersion < 3 || StartFullScreen,
            PreferredMonitor = Math.Max(0, PreferredMonitor),
            MouseCursorHideSeconds = Math.Clamp(MouseCursorHideSeconds, 1, 30)
        };
    }

    private static CameraSettings NormalizeCamera(CameraSettings camera, int slot, string defaultName)
    {
        var normalized = camera with
        {
            Slot = slot,
            Name = string.IsNullOrWhiteSpace(camera.Name) ? defaultName : camera.Name.Trim(),
            NetworkCacheMilliseconds = Math.Clamp(camera.NetworkCacheMilliseconds, 100, 10_000),
            StartupTimeoutSeconds = Math.Clamp(camera.StartupTimeoutSeconds, 8, 120),
            WatchdogTimeoutSeconds = Math.Clamp(camera.WatchdogTimeoutSeconds, 8, 120),
            MaximumReconnectBackoffSeconds = Math.Clamp(camera.MaximumReconnectBackoffSeconds, 5, 300)
        };
        return normalized.UsesStreamGridCompositePolicy()
            ? normalized with { Transport = RtspTransport.Tcp, NetworkCacheMilliseconds = 3000, LowLatency = false }
            : normalized;
    }

    private static (int Leading, int Trailing) NormalizeCropPair(int leading, int trailing)
    {
        leading = Math.Clamp(leading, 0, 80);
        trailing = Math.Clamp(trailing, 0, 80);
        if (leading + trailing <= 90) return (leading, trailing);
        trailing = 90 - leading;
        return (leading, Math.Max(0, trailing));
    }
}

public enum PictureInPicturePosition { TopLeft, TopRight, BottomLeft, BottomRight }
public enum DoorbellVideoSizing { Fit, Stretch }
public enum DoorbellViewportShape { Native, Square, RoundedSquare, Circle, Oval }

public sealed record DoorbellOverlaySettings
{
    public int HostCameraSlot { get; init; } = 2;
    public PictureInPicturePosition Position { get; init; } = PictureInPicturePosition.BottomLeft;
    // Retained for migration from schema 8 and earlier.
    public int SizePercent { get; init; } = 50;
    public int ViewportWidthPercent { get; init; } = 50;
    public int ViewportHeightPercent { get; init; } = 50;
    public DoorbellVideoSizing VideoSizing { get; init; } = DoorbellVideoSizing.Fit;
    public DoorbellViewportShape ViewportShape { get; init; } = DoorbellViewportShape.Native;
    public int HorizontalOffsetPercent { get; init; }
    public int VerticalOffsetPercent { get; init; }
    public int ZoomPercent { get; init; } = 100;
    public int CropLeftPercent { get; init; }
    public int CropRightPercent { get; init; }
    public int CropTopPercent { get; init; }
    public int CropBottomPercent { get; init; }
    public int ImageHorizontalPositionPercent { get; init; } = 50;
    public int ImageVerticalPositionPercent { get; init; } = 50;
    public CameraSettings Camera { get; init; } = AppSettings.CreateDoorbellCamera();
}

public sealed record DisplaySettings
{
    public bool StartFullScreen { get; init; } = true;
    public int PreferredMonitor { get; init; }
    public bool HideMouseCursor { get; init; } = true;
    public int MouseCursorHideSeconds { get; init; } = 3;
    public bool ShowCameraNames { get; init; } = true;
    public bool ShowCameraStats { get; init; } = true;
    public bool KeepViewerAlwaysOnTop { get; init; } = true;
}
