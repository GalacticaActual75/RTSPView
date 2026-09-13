using LibreHardwareMonitor.Hardware;
using RTSPView.Core;

namespace RTSPView.Hardware;

public interface ITemperatureSensors : IDisposable
{
    (double? Cpu, double? Gpu) Read();
    void Reset() { }
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

    public void Reset() { _computer?.Close(); _computer = null; }
    public void Dispose() => Reset();
}

