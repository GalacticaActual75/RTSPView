namespace RTSPView.Core;

/// <summary>Tracks decoded-frame progress, not merely a successful connection.</summary>
public sealed class StreamActivity
{
    private int _frames;
    private DateTimeOffset _sampleAt;
    public DateTimeOffset? LastFrameAt { get; private set; }
    public double FramesPerSecond { get; private set; }
    public void BeginAttempt(DateTimeOffset now)
    {
        _frames = 0;
        _sampleAt = now;
        FramesPerSecond = 0;
    }
    public void Observe(int frames, DateTimeOffset now)
    {
        var elapsed = (now - _sampleAt).TotalSeconds;
        var progressed = frames > _frames;
        FramesPerSecond = elapsed > 0 && progressed ? (frames - _frames) / elapsed : 0;
        if (progressed) LastFrameAt = now;
        _frames = frames;
        _sampleAt = now;
    }
    public string? Warning(DateTimeOffset now, DateTimeOffset attemptStartedAt)
    {
        var age = Math.Max(0, (now - (LastFrameAt ?? attemptStartedAt)).TotalSeconds);
        if (age < 5) return null;
        return LastFrameAt is null ? $"No video frames received for {(long)age} seconds"
            : $"Last frame received {(long)age} seconds ago";
    }
}
