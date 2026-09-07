using SpotMonitor.Core;
using SpotMonitor.Infrastructure;

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
            VideoSizing = DoorbellVideoSizing.Stretch,
            ViewportShape = DoorbellViewportShape.RoundedSquare,
            HorizontalOffsetPercent = 12,
            VerticalOffsetPercent = -8,
            ZoomPercent = 160,
            CropLeftPercent = 4,
            CropRightPercent = 6,
            CropTopPercent = 50,
            CropBottomPercent = 2,
            ImageHorizontalPositionPercent = 25,
            ImageVerticalPositionPercent = 80,
            Camera = AppSettings.CreateDoorbellCamera() with { Enabled = true, RtspUrl = "rtsp://door:door-secret@example.test/doorbell" }
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
    Check(recovered.DoorbellOverlay.VideoSizing == DoorbellVideoSizing.Stretch, "doorbell video sizing persistence");
    Check(recovered.DoorbellOverlay.ViewportShape == DoorbellViewportShape.RoundedSquare, "doorbell viewport shape persistence");
    Check(recovered.DoorbellOverlay.HorizontalOffsetPercent == 12 &&
          recovered.DoorbellOverlay.VerticalOffsetPercent == -8, "doorbell viewport offset persistence");
    Check(recovered.DoorbellOverlay.ZoomPercent == 160, "doorbell zoom persistence");
    Check(recovered.DoorbellOverlay.ViewportWidthPercent == 80 &&
          recovered.DoorbellOverlay.ViewportHeightPercent == 40, "doorbell viewport dimensions persistence");
    Check(recovered.DoorbellOverlay.CropTopPercent == 50 &&
          recovered.DoorbellOverlay.CropBottomPercent == 2 &&
          recovered.DoorbellOverlay.CropLeftPercent == 4 &&
          recovered.DoorbellOverlay.CropRightPercent == 6, "doorbell source crop persistence");
    Check(recovered.DoorbellOverlay.ImageHorizontalPositionPercent == 25 &&
          recovered.DoorbellOverlay.ImageVerticalPositionPercent == 80, "doorbell image position persistence");

    var migratedOverlay = (new AppSettings
    {
        SchemaVersion = 8,
        DoorbellOverlay = new DoorbellOverlaySettings { SizePercent = 73 }
    }).Normalize().DoorbellOverlay;
    Check(migratedOverlay.ViewportWidthPercent == 73 &&
          migratedOverlay.ViewportHeightPercent == 73, "schema 8 doorbell size migration");

    var normalizedOverlay = (new AppSettings
    {
        DoorbellOverlay = new DoorbellOverlaySettings
        {
            HostCameraSlot = 99,
            Position = (PictureInPicturePosition)999,
            SizePercent = 5,
            ViewportWidthPercent = 999,
            ViewportHeightPercent = -999,
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
    Check(normalizedOverlay.HorizontalOffsetPercent == 50 &&
          normalizedOverlay.VerticalOffsetPercent == -50, "doorbell viewport offset normalization");
    Check(normalizedOverlay.ZoomPercent == 300, "doorbell zoom normalization");
    Check(normalizedOverlay.CropLeftPercent == 80 &&
          normalizedOverlay.CropRightPercent == 10 &&
          normalizedOverlay.CropTopPercent == 80 &&
          normalizedOverlay.CropBottomPercent == 10, "doorbell source crop normalization");
    Check(normalizedOverlay.ImageHorizontalPositionPercent == 0 &&
          normalizedOverlay.ImageVerticalPositionPercent == 100, "doorbell image position normalization");
    Check(normalizedOverlay.Camera.Slot == 10 && normalizedOverlay.Camera.Name == "Doorbell", "doorbell camera normalization");

    var alignedCrop = DoorbellVideoTransform.CalculateCropWindow(
        2048, 1536, 500, 300, DoorbellVideoSizing.Stretch, DoorbellViewportShape.RoundedSquare,
        100, 0, 0, 54, 0, 50, 50);
    Check(alignedCrop == new DoorbellCropWindow(0, 830, 2048, 706),
        "doorbell crop aligns 4:2:0 source coordinates");
    Check(alignedCrop.ToVlcGeometry() == "2048x706+0+830", "doorbell VLC crop geometry");

    var centeredOval = DoorbellVideoTransform.CalculateCropWindow(
        2048, 1536, 730, 385, DoorbellVideoSizing.Fit, DoorbellViewportShape.Oval,
        100, 0, 0, 0, 0, 50, 50);
    Check(centeredOval.X == 0 && centeredOval.Width == 2048 && centeredOval.Y > 0 &&
          centeredOval.Y + centeredOval.Height < 1536, "doorbell fit crop removes letterbox region");

    var bottomRightZoom = DoorbellVideoTransform.CalculateCropWindow(
        2048, 1536, 730, 385, DoorbellVideoSizing.Fit, DoorbellViewportShape.Oval,
        200, 0, 0, 0, 0, 100, 100);
    Check(bottomRightZoom.X + bottomRightZoom.Width == 2048 &&
          bottomRightZoom.Y + bottomRightZoom.Height == 1536, "doorbell zoom can focus bottom-right");

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

    Console.WriteLine("Configuration checks passed: recovery, sanitized export, doorbell overlay normalization, schema rejection.");
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
