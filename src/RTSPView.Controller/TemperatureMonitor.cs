using System.Text.Json;
using RTSPView.Core;
using RTSPView.Hardware;

namespace RTSPView.Controller;

// Independent of browser polling. Settings are host-specific, like restart schedules.
public sealed class TemperatureMonitor : BackgroundService
{
    private readonly string _settingsPath, _statusPath;
    private readonly ITemperatureSensors _sensors;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _sensorGate = new(1, 1);
    private bool _suspended;
    private TemperatureStatus _latest = new();

    public TemperatureMonitor(string directory, ITemperatureSensors sensors, Action<string> log)
    {
        _settingsPath = Path.Combine(directory, "temperature-settings.json");
        _statusPath = Path.Combine(directory, "temperature-status.json");
        (_sensors, _log) = (sensors, log);
        try
        {
            if (File.Exists(_settingsPath))
            {
                var settings = JsonSerializer.Deserialize<TemperatureSettings>(File.ReadAllText(_settingsPath)) ?? throw new InvalidDataException();
                settings.Validate(); _latest = new() { Settings = settings };
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { _log("Temperature settings could not be read; warnings are disabled until settings are saved."); }
    }

    public TemperatureStatus Status(DateTimeOffset now)
    {
        var status = Volatile.Read(ref _latest);
        return status.IsFresh(now) ? status : status with { CpuC = null, GpuC = null };
    }

    public async Task ConfigureAsync(TemperatureSettings settings)
    {
        settings.Validate();
        await _gate.WaitAsync();
        try
        {
            Write(_settingsPath, settings);
            var status = _latest with { Settings = settings };
            Volatile.Write(ref _latest, status);
            Write(_statusPath, status);
        }
        finally { _gate.Release(); }
    }

    public async Task SampleAsync(DateTimeOffset now)
    {
        (double? Cpu, double? Gpu) reading;
        await _sensorGate.WaitAsync();
        try { reading = _suspended ? (null, null) : _sensors.Read(); } catch { reading = (null, null); }
        finally { _sensorGate.Release(); }
        await _gate.WaitAsync();
        try
        {
            var status = new TemperatureStatus { Timestamp = now, Settings = _latest.Settings,
                CpuC = TemperatureStatus.Valid(reading.Cpu) ? reading.Cpu : null,
                GpuC = TemperatureStatus.Valid(reading.Gpu) ? reading.Gpu : null };
            Volatile.Write(ref _latest, status);
            Write(_statusPath, status);
        }
        finally { _gate.Release(); }
    }

    public async Task SuspendAsync(bool suspended)
    {
        await _sensorGate.WaitAsync();
        try { _suspended = suspended; _sensors.Reset(); }
        finally { _sensorGate.Release(); }
        if (suspended) await SampleAsync(DateTimeOffset.UtcNow);
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value));
        File.Move(path + ".tmp", path, true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(100, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SampleAsync(DateTimeOffset.UtcNow); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { _log("Temperature status could not be published to the viewer."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public override void Dispose() { _sensors.Dispose(); base.Dispose(); }
}
