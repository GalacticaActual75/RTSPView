namespace RTSPView.Core;

public enum StreamSourceMode { Auto, Direct, Streamlink, YtDlp }

public static class StreamSource
{
    public static bool IsValidUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "rtsp" or "http" or "https" && !string.IsNullOrEmpty(uri.Host);

    public static void Validate(CameraSettings settings)
    {
        if (!Enum.IsDefined(settings.SourceMode)) throw new InvalidDataException("Invalid stream source mode.");
        if (settings.MaximumHeight is not (0 or 360 or 480 or 720 or 1080 or 1440 or 2160))
            throw new InvalidDataException("Select a supported stream quality limit.");
        if (string.IsNullOrWhiteSpace(settings.RtspUrl)) return;
        if (!IsValidUrl(settings.RtspUrl)) throw new InvalidDataException("Enter an RTSP, HTTP or HTTPS stream URL.");
        if (new Uri(settings.RtspUrl).Scheme == "rtsp" && settings.SourceMode is StreamSourceMode.Streamlink or StreamSourceMode.YtDlp)
            throw new InvalidDataException("RTSP sources require Auto or Direct mode.");
    }

    public static bool NeedsResolver(CameraSettings settings)
    {
        if (settings.SourceMode == StreamSourceMode.Direct) return false;
        var uri = new Uri(settings.RtspUrl);
        if (uri.Scheme == "rtsp") return false;
        if (settings.SourceMode != StreamSourceMode.Auto) return true;
        return !new[] { ".m3u8", ".mpd", ".mp4", ".mkv", ".ts", ".webm", ".mjpeg", ".mjpg" }
            .Any(extension => uri.AbsolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }
}
