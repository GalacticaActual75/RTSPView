namespace RTSPView.Core;

public enum RtspTransport { Auto, Tcp, Udp }

public sealed record CameraSettings
{
    public int Slot { get; init; } = 1;
    public string Name { get; init; } = "Camera 1";
    public string RtspUrl { get; init; } = string.Empty;
    // Connector identity is independent of the viewer slot; retained through edits.
    public string ScryptedId { get; init; } = "";
    public string ScryptedTopic { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public RtspTransport Transport { get; init; } = RtspTransport.Tcp;
    public bool LowLatency { get; init; }
    public bool DecodeAudio { get; init; }
    public int NetworkCacheMilliseconds { get; init; } = 1000;
    public int StartupTimeoutSeconds { get; init; } = 20;
    public int WatchdogTimeoutSeconds { get; init; } = 12;
    public int MaximumReconnectBackoffSeconds { get; init; } = 30;

    public bool CompositeStream { get; init; }
    public bool UsesStreamGridCompositePolicy() => CompositeStream;

    public RtspTransport EffectiveTransport => UsesStreamGridCompositePolicy() ? RtspTransport.Tcp : Transport;
    public int EffectiveNetworkCacheMilliseconds => UsesStreamGridCompositePolicy() ? 3000 : Math.Clamp(NetworkCacheMilliseconds, 100, 10_000);
    public bool EffectiveLowLatency => !UsesStreamGridCompositePolicy() && LowLatency;

    public IReadOnlyList<string> ToMediaOptions()
    {
        var options = new List<string> { $":network-caching={EffectiveNetworkCacheMilliseconds}" };
        if (!UsesStreamGridCompositePolicy()) options.Add(":clock-jitter=0");
        if (!DecodeAudio) options.Add(":no-audio");
        if (EffectiveTransport != RtspTransport.Auto)
            options.Add(EffectiveTransport == RtspTransport.Tcp ? ":rtsp-tcp" : ":rtsp-udp");
        if (EffectiveLowLatency)
        {
            options.Add(":live-caching=150");
            options.Add(":drop-late-frames");
            options.Add(":skip-frames");
        }
        return options;
    }
}
