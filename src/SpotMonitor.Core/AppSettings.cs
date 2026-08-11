namespace SpotMonitor.Core;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 5;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    // Retained for automatic migration from the Phase 1 settings file.
    public CameraSettings Camera { get; init; } = new();
    public IReadOnlyList<CameraSettings> Cameras { get; init; } = CreateCameraSlots();
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
        {
            var camera = normalized[index];
            var normalizedCamera = camera with
            {
                Slot = index + 1,
                Name = string.IsNullOrWhiteSpace(camera.Name) ? $"Camera {index + 1}" : camera.Name.Trim(),
                NetworkCacheMilliseconds = Math.Clamp(camera.NetworkCacheMilliseconds, 100, 10_000),
                StartupTimeoutSeconds = Math.Clamp(camera.StartupTimeoutSeconds, 8, 120),
                WatchdogTimeoutSeconds = Math.Clamp(camera.WatchdogTimeoutSeconds, 8, 120),
                MaximumReconnectBackoffSeconds = Math.Clamp(camera.MaximumReconnectBackoffSeconds, 5, 300)
            };
            if (normalizedCamera.UsesStreamGridCompositePolicy())
                normalizedCamera = normalizedCamera with { Transport = RtspTransport.Tcp, NetworkCacheMilliseconds = 3000, LowLatency = false };
            normalized[index] = normalizedCamera;
        }
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Cameras = normalized,
            StartFullScreen = SchemaVersion < 3 || StartFullScreen,
            PreferredMonitor = Math.Max(0, PreferredMonitor),
            MouseCursorHideSeconds = Math.Clamp(MouseCursorHideSeconds, 1, 30)
        };
    }
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
