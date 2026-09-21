namespace RTSPView.Core;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 16;
    // IDs 10–25 remain reserved for existing overlay streams.
    public static readonly int[] MainCameraSlots = [1,2,3,4,5,6,7,8,9,26,27,28,29,30,31,32];
    public IReadOnlyList<WallLayout> Layouts { get; init; } = [new()];
    public IReadOnlyList<WallLayout> AutomationViewLayouts { get; init; } = AutomationLayouts.Defaults();
    public string ActiveLayoutId { get; init; } = "default";
    public IReadOnlyList<WeatherOverlay> WeatherOverlays { get; init; } = [];
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    // Retained for automatic migration from the Phase 1 settings file.
    public CameraSettings Camera { get; init; } = new();
    public IReadOnlyList<CameraSettings> Cameras { get; init; } = CreateCameraSlots();
    // Player capacity is separate from camera entries the user has added.
    public int CameraCount { get; init; } = 9;
    public int[] DeletedCameraSlots { get; init; } = [];
    public DoorbellOverlaySettings DoorbellOverlay { get; init; } = new();
    public DoorbellOverlaySettings GarageOverlay { get; init; } = CreateGarageOverlay();
    public const int MaximumAdditionalOverlays = 14;
    public const int MaximumStreamSlot = 32;
    public IReadOnlyList<DoorbellOverlaySettings> AdditionalOverlays { get; init; } = [];
    public IReadOnlyList<int> DeletedOverlaySlots { get; init; } = [];
    public IEnumerable<DoorbellOverlaySettings> AllOverlays() => new[] { DoorbellOverlay, GarageOverlay }.Concat(AdditionalOverlays);
    public bool RequestHardwareDecoding { get; init; } = true;
    public bool StartFullScreen { get; init; } = true;
    public int PreferredMonitor { get; init; }
    public bool HideMouseCursor { get; init; } = true;
    public int MouseCursorHideSeconds { get; init; } = 3;
    public bool ShowCameraNames { get; init; } = true;
    public bool ShowCameraStats { get; init; } = true;
    public bool ShowTileBorders { get; init; } = true;
    public int[] DiagnosticsAutoOpenExcludedSlots { get; init; } = [];
    public bool KeepViewerAlwaysOnTop { get; init; } = true;
    public bool ShowHoverExitButton { get; init; }
    public SnapshotSettings Snapshots { get; init; } = new();

    public static IReadOnlyList<CameraSettings> CreateCameraSlots() =>
        MainCameraSlots.Select((slot, index) => new CameraSettings { Slot = slot, Name = $"Camera {index + 1}", Enabled = index < 9 }).ToArray();

    public static CameraSettings CreateDoorbellCamera() => new()
    {
        Slot = 10,
        Name = "Doorbell",
        Enabled = false
    };

    public static CameraSettings CreateGarageCamera() => new()
    {
        Slot = 11,
        Name = "Garage",
        Enabled = false
    };

    public static DoorbellOverlaySettings CreateGarageOverlay() => new()
    {
        HostCameraSlot = 3,
        Camera = CreateGarageCamera()
    };

    public AppSettings Normalize()
    {
        if (WeatherOverlays is null || WeatherOverlays.Count > 16 || WeatherOverlays.Any(o => o is null) || WeatherOverlays.Select(o => o.HostCameraSlot).Distinct().Count() != WeatherOverlays.Count)
            throw new InvalidDataException("Keep at most one weather overlay per camera.");
        foreach (var weather in WeatherOverlays) weather.Validate();
        var normalized = CreateCameraSlots().ToArray();
        foreach (var camera in Cameras.Take(16))
        {
            var index = MainCameraSlots.Contains(camera.Slot) ? Array.IndexOf(MainCameraSlots, camera.Slot) : Array.IndexOf(Cameras.ToArray(), camera);
            if (index is >= 0 and < 16) normalized[index] = camera with { Slot = MainCameraSlots[index] };
        }
        if (normalized.All(camera => string.IsNullOrWhiteSpace(camera.RtspUrl)) && !string.IsNullOrWhiteSpace(Camera.RtspUrl))
            normalized[0] = Camera with { Slot = 1 };
        for (var index = 0; index < normalized.Length; index++)
            normalized[index] = NormalizeCamera(normalized[index], MainCameraSlots[index], $"Camera {index + 1}");

        foreach (var slot in DeletedCameraSlots ?? [])
        {
            var index = Array.IndexOf(MainCameraSlots, slot);
            if (index >= 0) normalized[index] = normalized[index] with { Enabled = false, RtspUrl = "" };
        }
        var overlay = NormalizeOverlay(DoorbellOverlay ?? new DoorbellOverlaySettings(), 10, "Doorbell", normalized);
        var garageOverlay = NormalizeOverlay(GarageOverlay ?? CreateGarageOverlay(), 11, "Garage", normalized);
        var layouts = SchemaVersion < 15 ? new WallLayout[] { new() } : Layouts;
        var activeId = SchemaVersion < 15 ? "default" : ActiveLayoutId;
        WallLayout.Validate(layouts, activeId);
        if (layouts.SelectMany(l => l.Tiles).Where(t => t.Kind == "weather").Select(t => t.Weather!.CacheKey).Concat(WeatherOverlays.Where(o => o.Enabled).Select(o => o.Weather.CacheKey)).Distinct().Count() > 32)
            throw new InvalidDataException("Keep at most 32 different weather locations across saved layouts and overlays.");
        var automationLayouts = AutomationLayouts.Normalize(AutomationViewLayouts);
        var cameraCount = Math.Clamp(CameraCount, 9, MainCameraSlots.Length);
        for (var index = 9; index < normalized.Length; index++)
        {
            var camera = normalized[index];
            if (camera.Enabled || !string.IsNullOrWhiteSpace(camera.RtspUrl) || camera.Name != $"Camera {index + 1}" ||
                layouts.Any(layout => layout.Tiles.Any(tile => tile.CameraSlot == camera.Slot)))
                cameraCount = Math.Max(cameraCount, index + 1);
        }
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            DeletedCameraSlots = (DeletedCameraSlots ?? []).Where(MainCameraSlots.Contains).Distinct().ToArray(),
            DeletedOverlaySlots = (DeletedOverlaySlots ?? []).Where(s => s >= 10 && s < 12 + Math.Min((AdditionalOverlays ?? []).Count, MaximumAdditionalOverlays)).Distinct().ToArray(),
            DiagnosticsAutoOpenExcludedSlots = (DiagnosticsAutoOpenExcludedSlots ?? []).Where(slot => slot >= 1 && slot <= StreamCatalog.MaximumSlot).Distinct().Order().ToArray(),
            Snapshots = (Snapshots ?? new()).Normalize(),
            Cameras = normalized,
            CameraCount = cameraCount,
            Layouts = layouts,
            AutomationViewLayouts = automationLayouts,
            ActiveLayoutId = activeId,
            DoorbellOverlay = overlay,
            GarageOverlay = garageOverlay,
            AdditionalOverlays = (AdditionalOverlays ?? []).Take(MaximumAdditionalOverlays)
                .Select((item, index) => NormalizeOverlay(item ?? new(), index + 12, $"Overlay {index + 3}", normalized)).ToArray(),
            StartFullScreen = SchemaVersion < 3 || StartFullScreen,
            PreferredMonitor = Math.Max(0, PreferredMonitor),
            MouseCursorHideSeconds = Math.Clamp(MouseCursorHideSeconds, 1, 30)
        };
    }

    private DoorbellOverlaySettings NormalizeOverlay(
        DoorbellOverlaySettings overlay,
        int cameraSlot,
        string defaultName, IReadOnlyList<CameraSettings> sources)
    {
        if ((DeletedOverlaySlots ?? []).Contains(cameraSlot))
            overlay = new DoorbellOverlaySettings { Camera = new CameraSettings { Slot = cameraSlot, Name = defaultName, Enabled = false } };
        if (overlay.SourceCameraSlot != 0)
        {
            var source = sources.FirstOrDefault(c => c.Slot == overlay.SourceCameraSlot);
            if (source is null || (DeletedCameraSlots ?? []).Contains(overlay.SourceCameraSlot))
                throw new InvalidDataException("An overlay references an unavailable source stream. Select an existing main stream or use its own RTSP URL.");
            var identity = overlay.Camera ?? new CameraSettings { Name = defaultName, Enabled = false };
            // Copy connection settings only. Each overlay keeps its identity, visibility and its own renderer/transforms.
            overlay = overlay with { Camera = source with { Slot = cameraSlot, Name = identity.Name, Enabled = identity.Enabled,
                ScryptedId = identity.ScryptedId, ScryptedTopic = identity.ScryptedTopic } };
        }
        var position = Enum.IsDefined(overlay.Position) ? overlay.Position : PictureInPicturePosition.BottomLeft;
        var viewportShape = Enum.IsDefined(overlay.ViewportShape) ? overlay.ViewportShape : DoorbellViewportShape.Native;
        var customViewportIsValid = CustomViewportPathValidator.IsValid(
            overlay.CustomViewportPathData,
            overlay.CustomViewportViewBoxX,
            overlay.CustomViewportViewBoxY,
            overlay.CustomViewportViewBoxWidth,
            overlay.CustomViewportViewBoxHeight);
        if (viewportShape == DoorbellViewportShape.Custom && !customViewportIsValid)
            viewportShape = DoorbellViewportShape.Native;
        var viewportWidthPercent = SchemaVersion < 9 ? overlay.SizePercent : overlay.ViewportWidthPercent;
        var viewportHeightPercent = SchemaVersion < 9 ? overlay.SizePercent : overlay.ViewportHeightPercent;
        var viewportHorizontalPositionPercent = SchemaVersion < 11
            ? MigrateViewportPosition(position, overlay.HorizontalOffsetPercent, viewportWidthPercent, horizontal: true)
            : overlay.ViewportHorizontalPositionPercent;
        var viewportVerticalPositionPercent = SchemaVersion < 11
            ? MigrateViewportPosition(position, overlay.VerticalOffsetPercent, viewportHeightPercent, horizontal: false)
            : overlay.ViewportVerticalPositionPercent;
        return overlay with
        {
            HostCameraSlot = MainCameraSlots.Contains(overlay.HostCameraSlot) ? overlay.HostCameraSlot : 9,
            Position = position,
            SizePercent = Math.Clamp(overlay.SizePercent, 25, 90),
            ViewportWidthPercent = Math.Clamp(viewportWidthPercent, 10, 95),
            ViewportHeightPercent = Math.Clamp(viewportHeightPercent, 10, 95),
            ViewportHorizontalPositionPercent = Math.Clamp(viewportHorizontalPositionPercent, 0, 100),
            ViewportVerticalPositionPercent = Math.Clamp(viewportVerticalPositionPercent, 0, 100),
            ViewportOpacityPercent = Math.Clamp(overlay.ViewportOpacityPercent, 20, 100),
            VideoSizing = DoorbellVideoSizing.Fit,
            ViewportShape = viewportShape,
            CustomViewportSourceName = customViewportIsValid
                ? CustomViewportPathValidator.NormalizeSourceName(overlay.CustomViewportSourceName)
                : string.Empty,
            CustomViewportPathData = customViewportIsValid ? overlay.CustomViewportPathData.Trim() : string.Empty,
            CustomViewportViewBoxX = customViewportIsValid ? overlay.CustomViewportViewBoxX : 0,
            CustomViewportViewBoxY = customViewportIsValid ? overlay.CustomViewportViewBoxY : 0,
            CustomViewportViewBoxWidth = customViewportIsValid ? overlay.CustomViewportViewBoxWidth : 1,
            CustomViewportViewBoxHeight = customViewportIsValid ? overlay.CustomViewportViewBoxHeight : 1,
            CustomViewportRotationDegrees = Math.Clamp(overlay.CustomViewportRotationDegrees, -180, 180),
            HorizontalOffsetPercent = 0,
            VerticalOffsetPercent = 0,
            ZoomPercent = Math.Clamp(overlay.ZoomPercent, 100, 300),
            CropLeftPercent = 0,
            CropRightPercent = 0,
            CropTopPercent = 0,
            CropBottomPercent = 0,
            ImageHorizontalPositionPercent = Math.Clamp(overlay.ImageHorizontalPositionPercent, 0, 100),
            ImageVerticalPositionPercent = Math.Clamp(overlay.ImageVerticalPositionPercent, 0, 100),
            Camera = NormalizeCamera(
                overlay.Camera ?? (cameraSlot == 10 ? CreateDoorbellCamera() : CreateGarageCamera()),
                cameraSlot,
                defaultName)
        };
    }

    private static CameraSettings NormalizeCamera(CameraSettings camera, int slot, string defaultName)
    {
        var normalized = camera with
        {
            Slot = slot,
            Name = string.IsNullOrWhiteSpace(camera.Name) ? defaultName : camera.Name.Trim(),
            ScryptedId = camera.ScryptedId ?? "",
            ScryptedTopic = camera.ScryptedTopic ?? "",
            NetworkCacheMilliseconds = Math.Clamp(camera.NetworkCacheMilliseconds, 100, 10_000),
            StartupTimeoutSeconds = Math.Clamp(camera.StartupTimeoutSeconds, 8, 120),
            WatchdogTimeoutSeconds = Math.Clamp(camera.WatchdogTimeoutSeconds, 8, 120),
            MaximumReconnectBackoffSeconds = Math.Clamp(camera.MaximumReconnectBackoffSeconds, 5, 300)
        };
        return normalized.UsesStreamGridCompositePolicy()
            ? normalized with { Transport = RtspTransport.Tcp, NetworkCacheMilliseconds = 3000, LowLatency = false }
            : normalized;
    }

    private static int MigrateViewportPosition(
        PictureInPicturePosition position, int legacyOffsetPercent, int viewportSizePercent, bool horizontal)
    {
        var trailing = horizontal
            ? position is PictureInPicturePosition.TopRight or PictureInPicturePosition.BottomRight
            : position is PictureInPicturePosition.BottomLeft or PictureInPicturePosition.BottomRight;
        var availablePercent = 100 - Math.Clamp(viewportSizePercent, 10, 95);
        var legacyCoordinate = (trailing ? availablePercent : 0) + Math.Clamp(legacyOffsetPercent, -50, 50);
        return (int)Math.Round(Math.Clamp(legacyCoordinate, 0, availablePercent) / (double)availablePercent * 100);
    }
}

public enum PictureInPicturePosition { TopLeft, TopRight, BottomLeft, BottomRight }
public enum DoorbellVideoSizing { Fit, Stretch }
public enum DoorbellViewportShape { Native, Square, RoundedSquare, Circle, Oval, Custom }

public sealed record DoorbellOverlaySettings
{
    // Zero keeps an independent connection. A main-stream slot follows that saved connection.
    public int SourceCameraSlot { get; init; }
    public int HostCameraSlot { get; init; } = 2;
    public PictureInPicturePosition Position { get; init; } = PictureInPicturePosition.BottomLeft;
    // Retained for migration from schema 8 and earlier.
    public int SizePercent { get; init; } = 50;
    public int ViewportWidthPercent { get; init; } = 50;
    public int ViewportHeightPercent { get; init; } = 50;
    public int ViewportHorizontalPositionPercent { get; init; }
    public int ViewportVerticalPositionPercent { get; init; } = 100;
    public int ViewportOpacityPercent { get; init; } = 100;
    public bool ShowBorder { get; init; } = true;
    public DoorbellVideoSizing VideoSizing { get; init; } = DoorbellVideoSizing.Fit;
    public DoorbellViewportShape ViewportShape { get; init; } = DoorbellViewportShape.Native;
    public string CustomViewportSourceName { get; init; } = string.Empty;
    public string CustomViewportPathData { get; init; } = string.Empty;
    public double CustomViewportViewBoxX { get; init; }
    public double CustomViewportViewBoxY { get; init; }
    public double CustomViewportViewBoxWidth { get; init; } = 1;
    public double CustomViewportViewBoxHeight { get; init; } = 1;
    public int CustomViewportRotationDegrees { get; init; }
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
    public bool ShowTileBorders { get; init; } = true;
    public int[] DiagnosticsAutoOpenExcludedSlots { get; init; } = [];
    public bool KeepViewerAlwaysOnTop { get; init; } = true;
    public bool ShowHoverExitButton { get; init; }
}
