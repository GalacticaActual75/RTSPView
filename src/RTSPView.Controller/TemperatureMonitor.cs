using System.Text.Json;
using LibreHardwareMonitor.Hardware;
using RTSPView.Core;

namespace RTSPView.Controller;

public interface ITemperatureSensors : IDisposable
{
    (double? Cpu, double? Gpu) Read();
}

public sealed class HardwareTemperatureSensors : ITemperatureSensors
{
    private Computer? _computer;
    public (double? Cpu, double? Gpu) Read()
    {
        if (_computer is null)
        {
            var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            try { computer.Open(); _computer = computer; }
            catch { computer.Close(); throw; }
        }
        var cpu = new List<double>();
        var gpu = new List<double>();
        foreach (var hardware in _computer.Hardware)
        {
            try
            {
                var values = new List<double>();
                ReadHardware(hardware, values);
                if (hardware.HardwareType == HardwareType.Cpu) cpu.AddRange(values);
                else if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel) gpu.AddRange(values);
            }
            catch { /* One unavailable device must not hide the other device's readings. */ }
        }
        return (cpu.Count == 0 ? null : Math.Round(cpu.Max(), 1), gpu.Count == 0 ? null : Math.Round(gpu.Max(), 1));
    }

    private static void ReadHardware(IHardware hardware, List<double> values)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
            if (sensor.SensorType == SensorType.Temperature && sensor.Value is { } value && TemperatureStatus.Valid(value)) values.Add(value);
        foreach (var child in hardware.SubHardware) ReadHardware(child, values);
    }

    public void Dispose() => _computer?.Close();
}

// Independent of browser polling. Settings are host-specific, like restart schedules.
public sealed class TemperatureMonitor : BackgroundService
{
    private readonly string _settingsPath, _statusPath;
    private readonly ITemperatureSensors _sensors;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
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
        try { reading = _sensors.Read(); } catch { reading = (null, null); }
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
