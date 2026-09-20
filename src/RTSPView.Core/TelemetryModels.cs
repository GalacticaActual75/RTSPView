namespace RTSPView.Core;

public sealed record CameraTelemetry
{
    public int Slot { get; init; }
    public string Name { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public float Fps { get; init; }
    public double BitrateKbps { get; init; }
    public string? Codec { get; init; }
    public uint? Width { get; init; }
    public uint? Height { get; init; }
    public string Decoder { get; init; } = "Unknown";
    public DateTimeOffset? SnapshotCapturedAt { get; init; }
    public string? FrameWarning { get; init; }
    public int ReconnectCount { get; init; }
    public long? StreamUptimeSeconds { get; init; }
    public DateTimeOffset? StreamStartedAt { get; init; }
    public DateTimeOffset? LastFrameAt { get; init; }
    public DateTimeOffset? LastReconnectAt { get; init; }
    public string? LastError { get; init; }
}

public sealed record ViewerTelemetry
{
    public AutomationVisibility? Automation { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public bool? IsFullScreen { get; init; }
    public long ViewerUptimeSeconds { get; init; }
    public double ViewerMemoryMb { get; init; }
    public string HardwareDecoder { get; init; } = "Unknown";
    public IReadOnlyList<CameraTelemetry> Cameras { get; init; } = [];
}

public sealed record SystemTelemetry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public double? CpuPercent { get; init; }
    public double? RamPercent { get; init; }
    public double? RamUsedGb { get; init; }
    public double? RamTotalGb { get; init; }
    public double? GpuPercent { get; init; }
    public double? GpuVideoDecodePercent { get; init; }
    public double? GpuTemperatureC { get; init; }
    public double? CpuTemperatureC { get; init; }
    public double? NetworkReceiveMbps { get; init; }
    public double? NetworkSendMbps { get; init; }
}

public sealed record ApplianceTelemetry
{
    public bool ViewerConnected { get; init; }
    public bool ViewerRunning { get; init; }
    public bool ViewerStarting { get; init; }
    public bool ViewerPaused { get; init; }
    public ViewerTelemetry? Viewer { get; init; }
    public SystemTelemetry System { get; init; } = new();
}

public enum ViewerCommandType
{
    RestartCamera,
    RestartAllCameras,
    RestartViewer,
    EnterFullScreen,
    ExitFullScreen,
    CaptureCameraSnapshot,
    AutomationOverlays,
    SensorAutomation
}

public sealed record ViewerCommand(Guid Id, ViewerCommandType Type, int? Slot = null, AutomationPresentation? Automation = null, SensorPresentation? Sensors = null);
public sealed record ViewerCommandResult(Guid Id, bool Success, string Message)
{
    public bool ExitingIntentionally { get; init; }
}
