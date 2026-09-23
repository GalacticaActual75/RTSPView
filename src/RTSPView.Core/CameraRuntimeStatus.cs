namespace RTSPView.Core;

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
    Stopped,
    Resolving
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

    public CameraRuntimeStatus WithFrameProgress(DateTimeOffset? lastFrameAt) => this with
    {
        LastFrameAt = lastFrameAt,
        LastError = State == CameraConnectionState.Live && NextReconnectAt is null &&
            lastFrameAt is not null && (LastFrameAt is null || lastFrameAt > LastFrameAt) ? null : LastError
    };
}
