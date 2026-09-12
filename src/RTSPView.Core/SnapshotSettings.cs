namespace RTSPView.Core;

public sealed record SnapshotSettings
{
    public bool Enabled { get; init; }
    public double IntervalHours { get; init; } = 1;
    public SnapshotSettings Normalize() => this with
    {
        IntervalHours = double.IsFinite(IntervalHours) ? Math.Clamp(IntervalHours, 0.1, 168) : 1
    };
}

public sealed class SnapshotRefreshSchedule
{
    private SnapshotSettings? _settings;
    private DateTimeOffset _next;
    public bool IsDue(DateTimeOffset now, SnapshotSettings settings)
    {
        settings = settings.Normalize();
        if (_settings != settings)
        {
            _settings = settings;
            _next = now.AddHours(settings.IntervalHours);
            return false;
        }
        if (!settings.Enabled || now < _next) return false;
        _next = now.AddHours(settings.IntervalHours);
        return true;
    }
}
