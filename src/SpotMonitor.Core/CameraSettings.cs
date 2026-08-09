namespace SpotMonitor.Core;

public enum RtspTransport { Auto, Tcp, Udp }

public sealed record CameraSettings
{
    public int Slot { get; init; } = 1;
    public string Name { get; init; } = "Camera 1";
    public string RtspUrl { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public RtspTransport Transport { get; init; } = RtspTransport.Tcp;
    public bool LowLatency { get; init; }
    public bool DecodeAudio { get; init; }
    public int NetworkCacheMilliseconds { get; init; } = 1000;
    public int StartupTimeoutSeconds { get; init; } = 20;
    public int WatchdogTimeoutSeconds { get; init; } = 12;
    public int MaximumReconnectBackoffSeconds { get; init; } = 30;

    public IReadOnlyList<string> ToMediaOptions()
    {
        var options = new List<string> { $":network-caching={Math.Clamp(NetworkCacheMilliseconds, 100, 10_000)}", ":clock-jitter=0" };
        if (!DecodeAudio) options.Add(":no-audio");
        if (Transport != RtspTransport.Auto)
            options.Add(Transport == RtspTransport.Tcp ? ":rtsp-tcp" : ":rtsp-udp");
        if (LowLatency)
        {
            options.Add(":live-caching=150");
            options.Add(":drop-late-frames");
            options.Add(":skip-frames");
        }
        return options;
    }
}
