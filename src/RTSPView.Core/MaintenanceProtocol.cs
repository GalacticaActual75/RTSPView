namespace RTSPView.Core;

public static class MaintenanceProtocol
{
    public const string PipeName = "RTSPView.Maintenance.v1";
    public const string ServiceName = "RTSPViewMaintenance";
    public const string RegistryPath = @"SOFTWARE\RTSPView\Maintenance";
    public const string PawnVersion = "2.2.0";
    public const string PawnUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    public const string PawnSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
}

public sealed record MaintenanceRequest(string Command, Guid? OperationId = null);
public sealed record MaintenanceStatus
{
    public bool Available { get; init; }
    public bool PawnInstalled { get; init; }
    public string State { get; init; } = "unavailable";
    public string Message { get; init; } = "Enable the maintenance helper on this host to install PawnIO remotely.";
    public Guid? OperationId { get; init; }
    public bool RestartRequired { get; init; }
    public TemperatureStatus? Temperatures { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
