namespace RTSPView.Core;

public sealed record TemperatureSettings
{
    public bool ShowWarnings { get; init; }
    public bool CpuWarningEnabled { get; init; }
    public bool GpuWarningEnabled { get; init; }
    public double CpuMaxC { get; init; } = 90;
    public double GpuMaxC { get; init; } = 85;

    public void Validate()
    {
        if (!double.IsFinite(CpuMaxC) || !double.IsFinite(GpuMaxC) || CpuMaxC is < 1 or > 150 || GpuMaxC is < 1 or > 150)
            throw new ArgumentException("Temperature limits must be between 1 and 150 °C.");
    }
}

public sealed record TemperatureStatus
{
    public DateTimeOffset Timestamp { get; init; }
    public TemperatureSettings Settings { get; init; } = new();
    public double? CpuC { get; init; }
    public double? GpuC { get; init; }
    public bool IsFresh(DateTimeOffset now) => Timestamp <= now.AddSeconds(5) && Timestamp >= now.AddSeconds(-20);
    public bool CpuWarning(DateTimeOffset now) => IsFresh(now) && Settings is { ShowWarnings: true, CpuWarningEnabled: true } && Valid(CpuC) && CpuC > Settings.CpuMaxC;
    public bool GpuWarning(DateTimeOffset now) => IsFresh(now) && Settings is { ShowWarnings: true, GpuWarningEnabled: true } && Valid(GpuC) && GpuC > Settings.GpuMaxC;
    public static bool Valid(double? value) => value is { } temperature && double.IsFinite(temperature) && temperature is >= -50 and <= 200;
}
