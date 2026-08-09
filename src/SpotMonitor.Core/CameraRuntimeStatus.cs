namespace SpotMonitor.Core;

public enum CameraConnectionState
{
    Disabled,
    NotConfigured,
    Connecting,
    Buffering,
    Live,
    Reconnecting,
    Offline,
    StreamError,
    Stopped
}

public sealed record CameraRuntimeStatus
{
    public CameraConnectionState State { get; init; } = CameraConnectionState.Stopped;
    public int ReconnectCount { get; init; }
    public int ConsecutiveFailures { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
    public DateTimeOffset? LastFrameAt { get; init; }
    public DateTimeOffset? LastReconnectAt { get; init; }
    public DateTimeOffset? NextReconnectAt { get; init; }
    public string? LastError { get; init; }
}
