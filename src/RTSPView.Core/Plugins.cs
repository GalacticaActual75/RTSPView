namespace RTSPView.Core;

// Missing flags retain existing installations' behavior. Feature data stays in AppSettings.
public sealed record Plugins
{
    public bool Weather { get; init; } = true;
    public bool Aircraft { get; init; } = true;
    public bool YtDlp { get; init; } = true;
    public bool Streamlink { get; init; } = true;
    public bool Onvif { get; init; } = true;
    public bool PictureInPicture { get; init; } = true;
    public bool Automations { get; init; } = true;
    public bool AllowsSource(CameraSettings camera) => string.IsNullOrWhiteSpace(camera.RtspUrl) || !StreamSource.NeedsResolver(camera)
        || camera.SourceMode switch { StreamSourceMode.YtDlp => YtDlp, StreamSourceMode.Streamlink => Streamlink, _ => YtDlp || Streamlink };
}
