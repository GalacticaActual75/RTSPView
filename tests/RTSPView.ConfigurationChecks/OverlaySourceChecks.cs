using System.Text.Json;
using RTSPView.Core;
using RTSPView.Infrastructure;

internal static class OverlaySourceChecks
{
    public static async Task Run(string root)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var settings = new AppSettings().Normalize();
        var source = settings.Cameras[0] with { RtspUrl = "rtsp://user:secret@camera.example/live", Name = "Main feed", Enabled = false, NetworkCacheMilliseconds = 2300 };
        settings = (settings with { Cameras = settings.Cameras.Select(c => c.Slot == 1 ? source : c).ToArray(),
            DoorbellOverlay = settings.DoorbellOverlay with { SourceCameraSlot = 1, ZoomPercent = 175, ViewportOpacityPercent = 55,
                Camera = settings.DoorbellOverlay.Camera with { Name = "Independent crop", Enabled = true } } }).Normalize();
        Check(settings.DoorbellOverlay.Camera.RtspUrl == source.RtspUrl && settings.DoorbellOverlay.Camera.NetworkCacheMilliseconds == 2300, "Overlay did not resolve its saved source connection");
        Check(settings.DoorbellOverlay.Camera.Slot == 10 && settings.DoorbellOverlay.Camera.Name == "Independent crop" && settings.DoorbellOverlay.Camera.Enabled, "Link changed overlay identity or visibility");
        Check(settings.Cameras[0] == source, "Overlay link mutated main stream");
        var originalLayouts = JsonSerializer.Serialize(settings.Layouts);
        var changed = (settings with { DoorbellOverlay = settings.DoorbellOverlay with { ZoomPercent = 250, ImageHorizontalPositionPercent = 80, ViewportOpacityPercent = 30 } }).Normalize();
        Check(changed.Cameras[0] == source && JsonSerializer.Serialize(changed.Layouts) == originalLayouts, "Overlay framing changed the main stream or wall layout");
        changed = (changed with { Cameras = changed.Cameras.Select(c => c.Slot == 1 ? c with { RtspUrl = "rtsp://camera.example/new", Transport = RtspTransport.Udp } : c).ToArray() }).Normalize();
        Check(changed.DoorbellOverlay.Camera.RtspUrl.EndsWith("/new") && changed.DoorbellOverlay.Camera.Transport == RtspTransport.Udp && changed.DoorbellOverlay.ZoomPercent == 250, "Connection refresh lost independent framing");
        var store = new JsonSettingsStore(Path.Combine(root, "linked-source.json"));
        await store.SaveAsync(settings);
        var restored = await store.LoadAsync();
        Check(restored.DoorbellOverlay.SourceCameraSlot == 1 && restored.DoorbellOverlay.Camera.RtspUrl == source.RtspUrl, "Source link did not survive reload");
        var sanitized = JsonSettingsStore.WithoutCredentials(restored);
        Check(sanitized.DoorbellOverlay.SourceCameraSlot == 1 && !JsonSerializer.Serialize(sanitized).Contains("secret"), "Linked export leaked credentials or lost reference");
        Check(JsonSettingsStore.ParseImport(JsonSerializer.Serialize(sanitized)).DoorbellOverlay.SourceCameraSlot == 1, "Import lost source link");
        try { StreamCatalog.DeleteCamera(settings, 1); throw new Exception("Linked source deletion accepted"); } catch (InvalidDataException) { }
        try { (settings with { DoorbellOverlay = settings.DoorbellOverlay with { SourceCameraSlot = 33 } }).Normalize(); throw new Exception("Recursive overlay source accepted"); } catch (InvalidDataException) { }
        var detached = (settings with { DoorbellOverlay = settings.DoorbellOverlay with { SourceCameraSlot = 0 } }).Normalize();
        Check(detached.DoorbellOverlay.Camera.RtspUrl == source.RtspUrl, "Detaching lost the independent connection");
        Check(StreamCatalog.DeleteCamera(detached, 1).DeletedCameraSlots.Contains(1), "Detaching did not release deletion guard");
        var cleared = (settings with { Cameras = settings.Cameras.Select(c => c.Slot == 1 ? c with { RtspUrl = "" } : c).ToArray() }).Normalize();
        Check(cleared.DoorbellOverlay.Camera.RtspUrl == "", "Source removal left a stale URL playing in the overlay");
        Console.WriteLine("PASS linked overlay sources: duplicate URLs, independent framing/identity, connection propagation, reload/export/import, source deletion guards and detachment.");
    }
}
