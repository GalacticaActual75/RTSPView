using System.Text.Json;
using RTSPView.Core;
using RTSPView.Infrastructure;

internal static class StreamSourceChecks
{
    public static async Task Run(string root)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        foreach (var url in new[] { "rtsp://camera.example/live", "https://media.example/live.m3u8?token=private", "http://media.example/video.mp4", "https://media.example/live.mpd" })
        {
            var camera = new CameraSettings { RtspUrl = url };
            StreamSource.Validate(camera);
            Check(!StreamSource.NeedsResolver(camera), "Direct media incorrectly routed to website helper");
            using var resolved = await StreamResolver.ResolveAsync(camera, CancellationToken.None);
            Check(resolved.Uri == new Uri(url), "Direct URL was changed");
            Check(camera.ToMediaOptions().Any(o => o.StartsWith(":rtsp-")) == url.StartsWith("rtsp:"), "RTSP options leaked into HTTP playback");
        }
        var website = new CameraSettings { RtspUrl = "https://site.example/watch?v=123", SourceMode = StreamSourceMode.YtDlp, MaximumHeight = 1080 };
        Check(StreamSource.NeedsResolver(website), "Website skipped resolver");
        Check(!StreamSource.NeedsResolver(website with { SourceMode = StreamSourceMode.Direct }), "Explicit Direct ignored");
        foreach (var camera in new[] { website with { RtspUrl = "file:///C:/private.mp4" }, website with { RtspUrl = "ftp://site.example/test" }, website with { SourceMode = (StreamSourceMode)99 }, website with { MaximumHeight = -1 }, website with { RtspUrl = "rtsp://camera.example/live" } })
        {
            try { StreamSource.Validate(camera); throw new Exception("Invalid source accepted"); }
            catch (InvalidDataException) { }
        }
        var settings = new AppSettings().Normalize();
        settings = settings with { Cameras = settings.Cameras.Select(c => c.Slot == 1 ? website : c).ToArray(), DoorbellOverlay = settings.DoorbellOverlay with { SourceCameraSlot = 1 } };
        var store = new JsonSettingsStore(Path.Combine(root, "stream-sources.json"));
        await store.SaveAsync(settings);
        var restored = await store.LoadAsync();
        Check(restored.Cameras[0].SourceMode == StreamSourceMode.YtDlp && restored.Cameras[0].MaximumHeight == 1080, "Source preferences lost on save");
        Check(restored.DoorbellOverlay.Camera.SourceMode == StreamSourceMode.YtDlp && restored.DoorbellOverlay.Camera.MaximumHeight == 1080, "Linked overlay lost source preferences");
        var imported = JsonSettingsStore.ParseImport(JsonSerializer.Serialize(restored));
        Check(imported.Cameras[0].RtspUrl == website.RtspUrl, "Full export/import changed source URL");
        var legacy = JsonSerializer.Deserialize<CameraSettings>("{\"RtspUrl\":\"rtsp://camera.example/live\"}")!;
        Check(legacy.SourceMode == StreamSourceMode.Auto && !StreamSource.NeedsResolver(legacy), "Legacy RTSP defaults changed");
        try { await StreamResolver.ResolveAsync(website, CancellationToken.None, Path.Combine(root, "missing.exe")); throw new Exception("Missing helper accepted"); }
        catch (InvalidOperationException error) { Check(error.Message.Contains("helper missing"), "Missing helper has no actionable error"); }
        Console.WriteLine("PASS direct/web routing, protocol-specific options, validation, legacy settings, linked overlays and source persistence");
    }
}
