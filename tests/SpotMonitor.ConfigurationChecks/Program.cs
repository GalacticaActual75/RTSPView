using SpotMonitor.Core;
using SpotMonitor.Infrastructure;

var root = Path.Combine(Path.GetTempPath(), "SpotMonitor-ConfigurationChecks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var path = Path.Combine(root, "settings.json");
    var store = new JsonSettingsStore(path);
    var first = new AppSettings { Cameras = AppSettings.CreateCameraSlots().Select((camera, index) => index == 0 ? camera with { Name = "First", RtspUrl = "rtsp://user:secret@example.test/live" } : camera).ToArray() };
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

    Console.WriteLine("Configuration checks passed: atomic backup recovery, sanitized export, schema rejection.");
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
