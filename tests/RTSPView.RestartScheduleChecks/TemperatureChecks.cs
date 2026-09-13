using System.Text.Json;
using RTSPView.Controller;
using RTSPView.Core;

internal static class TemperatureChecks
{
    public static async Task Run(string root)
    {
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(root, "temperatures");
        var sensors = new FakeSensors();
        using var monitor = new TemperatureMonitor(path, sensors, _ => {});
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        Check(!monitor.Status(now).Settings.ShowWarnings, "temperature warnings default off");
        var settings = new TemperatureSettings { ShowWarnings = true, CpuWarningEnabled = true, GpuWarningEnabled = true, CpuMaxC = 80, GpuMaxC = 75 };
        await monitor.ConfigureAsync(settings);
        sensors.Reading = (81, 76);
        await monitor.SampleAsync(now);
        Check(monitor.Status(now).CpuWarning(now) && monitor.Status(now).GpuWarning(now), "both limits trigger independently");
        var wall = JsonSerializer.Deserialize<TemperatureStatus>(File.ReadAllText(Path.Combine(path, "temperature-status.json")))!;
        Check(wall.CpuWarning(now), "viewer notice published without browser requests");
        await monitor.ConfigureAsync(settings with { ShowWarnings = false });
        Check(!monitor.Status(now).CpuWarning(now) && !monitor.Status(now).GpuWarning(now) && monitor.Status(now).CpuC == 81, "master off hides warnings but preserves temperatures");
        wall = JsonSerializer.Deserialize<TemperatureStatus>(File.ReadAllText(Path.Combine(path, "temperature-status.json")))!;
        Check(!wall.CpuWarning(now), "master off immediately published to viewer");
        await monitor.ConfigureAsync(settings with { CpuWarningEnabled = false });
        Check(!monitor.Status(now).CpuWarning(now) && monitor.Status(now).GpuWarning(now), "per-device off independent of other device");
        await monitor.ConfigureAsync(settings);
        sensors.Reading = (80, 74);
        await monitor.SampleAsync(now);
        Check(!monitor.Status(now).CpuWarning(now) && !monitor.Status(now).GpuWarning(now), "at/below limit clears warning");
        sensors.Reading = (99, null);
        await monitor.SampleAsync(now);
        Check(monitor.Status(now).CpuWarning(now) && !monitor.Status(now).GpuWarning(now), "unavailable GPU does not hide CPU warning");
        Check(monitor.Status(now.AddSeconds(21)).CpuC is null && !monitor.Status(now.AddSeconds(21)).CpuWarning(now.AddSeconds(21)), "stale samples never look current or warn");
        Check(!monitor.Status(now).CpuWarning(now.AddSeconds(-10)), "future timestamp rejected");
        sensors.Fail = true;
        await monitor.SampleAsync(now);
        Check(monitor.Status(now).CpuC is null && !monitor.Status(now).CpuWarning(now), "sensor failure clears old hot reading");
        sensors.Fail = false; sensors.Reading = (double.NaN, double.PositiveInfinity);
        await monitor.SampleAsync(now);
        Check(monitor.Status(now).CpuC is null && monitor.Status(now).GpuC is null, "invalid values unavailable");
        foreach (var limit in new[] { double.NaN, double.PositiveInfinity, 0, 151 })
        {
            try { await monitor.ConfigureAsync(settings with { CpuMaxC = limit }); throw new Exception("invalid temperature limit accepted"); } catch (ArgumentException) { }
        }
        using var reopened = new TemperatureMonitor(path, new FakeSensors(), _ => {});
        Check(reopened.Status(now).Settings == settings && reopened.Status(now).CpuC is null, "settings persist but old readings are not restored");
        Console.WriteLine("PASS temperature limits, master/per-device toggles, recovery, missing/stale/invalid sensors and persistence (fake sensors only).");
    }
    private sealed class FakeSensors : ITemperatureSensors
    {
        public (double? Cpu, double? Gpu) Reading;
        public bool Fail;
        public (double? Cpu, double? Gpu) Read() => Fail ? throw new IOException() : Reading;
        public void Dispose() { }
    }
}
