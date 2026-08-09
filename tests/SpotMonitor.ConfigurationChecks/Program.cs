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
