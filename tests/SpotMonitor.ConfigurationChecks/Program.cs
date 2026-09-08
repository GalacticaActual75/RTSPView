using SpotMonitor.Core;
using SpotMonitor.Infrastructure;

const string sampleCustomViewportPath = "m344.6614 99.874016l90.734924 14.3622055l142.30185 35.249344l75.72174 30.679794l71.15228 39.165344l56.136475 40.469833l1.9580078 291.13385l-744.1522 -1.3044434l-3.2624664 -159.2756l23.498688 -122.71918z";
var root = Path.Combine(Path.GetTempPath(), "SpotMonitor-ConfigurationChecks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var path = Path.Combine(root, "settings.json");
    var store = new JsonSettingsStore(path);
    var first = new AppSettings
    {
        Cameras = AppSettings.CreateCameraSlots().Select((camera, index) => index == 0 ? camera with { Name = "First", RtspUrl = "rtsp://user:secret@example.test/live" } : camera).ToArray(),
        DoorbellOverlay = new DoorbellOverlaySettings
        {
            HostCameraSlot = 2,
            Position = PictureInPicturePosition.BottomLeft,
            SizePercent = 50,
            ViewportWidthPercent = 80,
            ViewportHeightPercent = 40,
            ViewportHorizontalPositionPercent = 35,
            ViewportVerticalPositionPercent = 75,
            ViewportShape = DoorbellViewportShape.RoundedSquare,
            ZoomPercent = 160,
            ImageHorizontalPositionPercent = 25,
            ImageVerticalPositionPercent = 80,
            Camera = AppSettings.CreateDoorbellCamera() with { Enabled = true, RtspUrl = "rtsp://door:door-secret@example.test/doorbell" }
        },
        GarageOverlay = AppSettings.CreateGarageOverlay() with
        {
            HostCameraSlot = 3,
            ViewportWidthPercent = 65,
            ViewportHeightPercent = 45,
            ViewportHorizontalPositionPercent = 80,
            ViewportVerticalPositionPercent = 20,
            ViewportOpacityPercent = 63,
            ViewportShape = DoorbellViewportShape.Custom,
            CustomViewportSourceName = @"C:\fakepath\Untitled drawing (1).svg",
            CustomViewportPathData = sampleCustomViewportPath,
            CustomViewportViewBoxX = 35.251968,
            CustomViewportViewBoxY = 99.874016,
            CustomViewportViewBoxWidth = 747.4147,
            CustomViewportViewBoxHeight = 451.06082,
            ZoomPercent = 175,
            ImageHorizontalPositionPercent = 70,
            ImageVerticalPositionPercent = 30,
            Camera = AppSettings.CreateGarageCamera() with { Enabled = true, RtspUrl = "rtsp://garage:garage-secret@example.test/garage" }
        }
    };
    await store.SaveAsync(first);
    var second = first with { Cameras = first.Cameras.Select((camera, index) => index == 0 ? camera with { Name = "Second" } : camera).ToArray() };
    await store.SaveAsync(second);
    File.WriteAllText(path, "{broken-json");
    var recovered = await store.LoadAsync();
    Check(recovered.Cameras[0].Name == "First", "backup recovery");

    var export = Path.Combine(root, "export.json");
    await store.ExportWithoutCredentialsAsync(first, export);
    var exportText = await File.ReadAllTextAsync(export);
    Check(!exportText.Contains("secret", StringComparison.Ordinal), "credential-free export");
    Check(exportText.Contains("example.test", StringComparison.Ordinal), "export retains endpoint");
    Check(recovered.DoorbellOverlay.Camera.Slot == 10, "doorbell overlay backup recovery");
    Check(recovered.DoorbellOverlay.VideoSizing == DoorbellVideoSizing.Fit, "doorbell aspect-preserving sizing persistence");
    Check(recovered.DoorbellOverlay.ViewportShape == DoorbellViewportShape.RoundedSquare, "doorbell viewport shape persistence");
    Check(recovered.DoorbellOverlay.ViewportHorizontalPositionPercent == 35 &&
          recovered.DoorbellOverlay.ViewportVerticalPositionPercent == 75, "doorbell viewport position persistence");
    Check(recovered.DoorbellOverlay.ZoomPercent == 160, "doorbell zoom persistence");
    Check(recovered.DoorbellOverlay.ViewportWidthPercent == 80 &&
          recovered.DoorbellOverlay.ViewportHeightPercent == 40, "doorbell viewport dimensions persistence");
    Check(recovered.DoorbellOverlay.ImageHorizontalPositionPercent == 25 &&
          recovered.DoorbellOverlay.ImageVerticalPositionPercent == 80, "doorbell image position persistence");
    Check(recovered.GarageOverlay.Camera.Slot == 11, "garage overlay backup recovery");
    Check(recovered.GarageOverlay.HostCameraSlot == 3, "garage host camera persistence");
    Check(recovered.GarageOverlay.ViewportShape == DoorbellViewportShape.Custom,
        "garage viewport shape persistence");
    Check(recovered.GarageOverlay.CustomViewportSourceName == "Untitled drawing (1).svg" &&
          recovered.GarageOverlay.CustomViewportPathData == sampleCustomViewportPath,
        "custom SVG path and sanitized filename persistence");
    Check(recovered.GarageOverlay.CustomViewportViewBoxWidth == 747.4147 &&
          recovered.GarageOverlay.CustomViewportViewBoxHeight == 451.06082,
        "custom SVG normalized bounds persistence");
    Check(recovered.GarageOverlay.ViewportWidthPercent == 65 &&
          recovered.GarageOverlay.ViewportHeightPercent == 45, "garage viewport dimensions persistence");
    Check(recovered.GarageOverlay.ViewportHorizontalPositionPercent == 80 &&
          recovered.GarageOverlay.ViewportVerticalPositionPercent == 20, "garage viewport position persistence");
    Check(recovered.GarageOverlay.ViewportOpacityPercent == 63, "garage viewport opacity persistence");
    Check(recovered.GarageOverlay.ZoomPercent == 175 &&
          recovered.GarageOverlay.ImageHorizontalPositionPercent == 70 &&
          recovered.GarageOverlay.ImageVerticalPositionPercent == 30, "garage framing persistence");

    var migratedGarage = (new AppSettings
    {
        SchemaVersion = 11,
        GarageOverlay = null!
    }).Normalize().GarageOverlay;
    Check(migratedGarage.HostCameraSlot == 3 &&
          migratedGarage.Camera.Slot == 11 &&
          migratedGarage.Camera.Name == "Garage" &&
          !migratedGarage.Camera.Enabled, "schema 11 creates disabled Garage overlay on Camera 3");
    Check((new AppSettings { SchemaVersion = 12 }).Normalize().DoorbellOverlay.ViewportOpacityPercent == 100 &&
          (new AppSettings { SchemaVersion = 12 }).Normalize().GarageOverlay.ViewportOpacityPercent == 100,
        "schema 12 overlays migrate to fully opaque viewports");

    Check(CustomViewportPathValidator.IsValid(
            sampleCustomViewportPath, 35.251968, 99.874016, 747.4147, 451.06082),
        "uploaded sample SVG path validation");
    Check(!CustomViewportPathValidator.IsValid("M0 0L10 10<script>", 0, 0, 10, 10),
        "custom SVG executable markup rejection");
    Check(!CustomViewportPathValidator.IsValid("M0 0L10 10", 0, 0, 0, 10),
        "custom SVG invalid bounds rejection");
    var invalidCustomViewport = (new AppSettings
    {
        DoorbellOverlay = new DoorbellOverlaySettings
        {
            ViewportShape = DoorbellViewportShape.Custom,
            CustomViewportPathData = "not-svg-path"
        }
    }).Normalize().DoorbellOverlay;
    Check(invalidCustomViewport.ViewportShape == DoorbellViewportShape.Native &&
          invalidCustomViewport.CustomViewportPathData.Length == 0,
        "invalid custom SVG falls back to rectangle safely");

    var migratedOverlay = (new AppSettings
    {
        SchemaVersion = 8,
        DoorbellOverlay = new DoorbellOverlaySettings { SizePercent = 73 }
    }).Normalize().DoorbellOverlay;
    Check(migratedOverlay.ViewportWidthPercent == 73 &&
          migratedOverlay.ViewportHeightPercent == 73, "schema 8 doorbell size migration");
    Check(migratedOverlay.ViewportHorizontalPositionPercent == 0 &&
          migratedOverlay.ViewportVerticalPositionPercent == 100, "schema 8 doorbell position migration");

    var migratedLegacyFraming = (new AppSettings
    {
        SchemaVersion = 10,
        DoorbellOverlay = new DoorbellOverlaySettings
        {
            Position = PictureInPicturePosition.BottomRight,
            ViewportWidthPercent = 73,
            ViewportHeightPercent = 77,
            HorizontalOffsetPercent = -2,
            VerticalOffsetPercent = -2,
            VideoSizing = DoorbellVideoSizing.Stretch,
            CropTopPercent = 54
        }
    }).Normalize().DoorbellOverlay;
    Check(migratedLegacyFraming.ViewportHorizontalPositionPercent == 93 &&
          migratedLegacyFraming.ViewportVerticalPositionPercent == 91, "schema 10 viewport anchor migration");
    Check(migratedLegacyFraming.VideoSizing == DoorbellVideoSizing.Fit &&
          migratedLegacyFraming.HorizontalOffsetPercent == 0 &&
          migratedLegacyFraming.CropTopPercent == 0, "schema 10 framing model migration");

    var normalizedOverlay = (new AppSettings
    {
        DoorbellOverlay = new DoorbellOverlaySettings
        {
            HostCameraSlot = 99,
            Position = (PictureInPicturePosition)999,
            SizePercent = 5,
            ViewportWidthPercent = 999,
            ViewportHeightPercent = -999,
            ViewportHorizontalPositionPercent = -999,
            ViewportVerticalPositionPercent = 999,
            ViewportOpacityPercent = -999,
            VideoSizing = (DoorbellVideoSizing)999,
            ViewportShape = (DoorbellViewportShape)999,
            HorizontalOffsetPercent = 999,
            VerticalOffsetPercent = -999,
            ZoomPercent = 999,
            CropLeftPercent = 80,
            CropRightPercent = 80,
            CropTopPercent = 80,
            CropBottomPercent = 80,
            ImageHorizontalPositionPercent = -999,
            ImageVerticalPositionPercent = 999,
            Camera = AppSettings.CreateDoorbellCamera() with { Name = "  " }
        }
    }).Normalize().DoorbellOverlay;
    Check(normalizedOverlay.HostCameraSlot == 9, "doorbell host camera normalization");
    Check(normalizedOverlay.Position == PictureInPicturePosition.BottomLeft, "doorbell corner normalization");
    Check(normalizedOverlay.SizePercent == 25, "doorbell size normalization");
    Check(normalizedOverlay.ViewportWidthPercent == 95 &&
          normalizedOverlay.ViewportHeightPercent == 10, "doorbell viewport dimension normalization");
    Check(normalizedOverlay.VideoSizing == DoorbellVideoSizing.Fit, "doorbell video sizing normalization");
    Check(normalizedOverlay.ViewportShape == DoorbellViewportShape.Native, "doorbell viewport shape normalization");
    Check(normalizedOverlay.ViewportHorizontalPositionPercent == 0 &&
          normalizedOverlay.ViewportVerticalPositionPercent == 100, "doorbell viewport position normalization");
    Check(normalizedOverlay.ViewportOpacityPercent == 20, "doorbell viewport opacity normalization");
    Check(normalizedOverlay.HorizontalOffsetPercent == 0 &&
          normalizedOverlay.VerticalOffsetPercent == 0, "legacy doorbell offsets are cleared");
    Check(normalizedOverlay.ZoomPercent == 300, "doorbell zoom normalization");
    Check(normalizedOverlay.CropLeftPercent == 0 &&
          normalizedOverlay.CropRightPercent == 0 &&
          normalizedOverlay.CropTopPercent == 0 &&
          normalizedOverlay.CropBottomPercent == 0, "legacy doorbell crops are cleared");
    Check(normalizedOverlay.ImageHorizontalPositionPercent == 0 &&
          normalizedOverlay.ImageVerticalPositionPercent == 100, "doorbell image position normalization");
    Check(normalizedOverlay.Camera.Slot == 10 && normalizedOverlay.Camera.Name == "Doorbell", "doorbell camera normalization");

    var alignedCrop = DoorbellVideoTransform.CalculateCropWindow(
        2048, 1536, 500, 300, 100, 50, 50);
    Check(alignedCrop == new DoorbellCropWindow(0, 154, 2048, 1228),
        "doorbell cover crop aligns 4:2:0 source coordinates");
    Check(alignedCrop.ToVlcGeometry() == "2048x1228+0+154", "doorbell VLC crop geometry");

    var centeredOval = DoorbellVideoTransform.CalculateCropWindow(
        2048, 1536, 730, 385, 100, 50, 50);
    Check(centeredOval.X == 0 && centeredOval.Width == 2048 && centeredOval.Y > 0 &&
          centeredOval.Y + centeredOval.Height < 1536, "doorbell cover crop removes letterbox region");
    Check(Math.Abs(centeredOval.Width / (double)centeredOval.Height - 730d / 385d) < 0.002,
        "doorbell crop matches viewport aspect ratio");

    var bottomRightZoom = DoorbellVideoTransform.CalculateCropWindow(
        2048, 1536, 730, 385, 200, 100, 100);
    Check(bottomRightZoom.X + bottomRightZoom.Width == 2048 &&
          bottomRightZoom.Y + bottomRightZoom.Height == 1536, "doorbell zoom can focus bottom-right");

    var liveLayout = DoorbellVideoTransform.CalculateLayout(
        2048, 1536, 730, 385, 175, 36, 88);
    Check(Math.Abs(liveLayout.RenderWidth / liveLayout.RenderHeight - 2048d / 1536d) < 0.000001,
        "doorbell native surface preserves source aspect ratio");
    Check(Math.Abs(liveLayout.SourceX - (-liveLayout.OffsetX / (liveLayout.RenderWidth / 2048d))) < 0.000001 &&
          Math.Abs(liveLayout.SourceY - (-liveLayout.OffsetY / (liveLayout.RenderHeight / 1536d))) < 0.000001,
        "doorbell preview crop and live surface use the same pan calculation");
    Check(Math.Abs(liveLayout.SourceWidth / liveLayout.SourceHeight - 730d / 385d) < 0.000001,
        "doorbell visible source matches viewport aspect ratio without bars");

    Check((int)ViewerCommandType.RestartCamera == 0 &&
          (int)ViewerCommandType.RestartAllCameras == 1 &&
          (int)ViewerCommandType.RestartViewer == 2 &&
          (int)ViewerCommandType.EnterFullScreen == 3 &&
          (int)ViewerCommandType.ExitFullScreen == 4 &&
          (int)ViewerCommandType.CaptureCameraSnapshot == 5,
        "viewer command protocol keeps existing numeric values stable");

    var grid = new CameraSettings { RtspUrl = "rtsp://camera.example:8554/grid1", Transport = RtspTransport.Udp, NetworkCacheMilliseconds = 100, LowLatency = true };
    var gridOptions = grid.ToMediaOptions();
    Check(grid.EffectiveTransport == RtspTransport.Tcp, "StreamGrid forces TCP");
    Check(grid.EffectiveNetworkCacheMilliseconds == 3000, "StreamGrid forces 3000 ms cache");
    Check(!grid.EffectiveLowLatency, "StreamGrid disables low latency");
    Check(gridOptions.Contains(":rtsp-tcp") && gridOptions.Contains(":network-caching=3000"), "StreamGrid per-media options");
    Check(!gridOptions.Contains(":rtsp-udp") && !gridOptions.Contains(":clock-jitter=0") && !gridOptions.Contains(":drop-late-frames"), "StreamGrid excludes conflicting options");

    var future = Path.Combine(root, "future.json");
    await File.WriteAllTextAsync(future, "{\"SchemaVersion\":999}");
    try { await store.ImportAsync(future); throw new InvalidOperationException("future schema was accepted"); }
    catch (InvalidDataException) { }

    Console.WriteLine("Configuration checks passed: recovery, sanitized export, dual-overlay normalization, schema rejection.");
}
finally
{
    if (root.StartsWith(Path.Combine(Path.GetTempPath(), "SpotMonitor-ConfigurationChecks"), StringComparison.OrdinalIgnoreCase))
        Directory.Delete(root, true);
}

static void Check(bool condition, string check)
{
    if (!condition) throw new InvalidOperationException($"Failed: {check}");
}
