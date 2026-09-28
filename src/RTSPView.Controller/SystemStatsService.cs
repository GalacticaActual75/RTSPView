using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

public sealed class SystemStatsService(string directory, SystemMetricsCollector metrics, TemperatureMonitor temperatures, RollingFileLogger log) : BackgroundService
{
    private SystemTelemetry? _latest;
    public SystemTelemetry? Latest => Volatile.Read(ref _latest) is { } sample && SystemStatsOptions.IsFresh(sample, DateTimeOffset.UtcNow) ? sample : null;
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => PluginRunner.RunAsync(directory, p => p.SystemStats, async token =>
    {
        while (!token.IsCancellationRequested)
        {
            var temperature = temperatures.Status(DateTimeOffset.UtcNow);
            var sample = metrics.GetSnapshot() with { CpuTemperatureC = temperature.CpuC, GpuTemperatureC = temperature.GpuC };
            Volatile.Write(ref _latest, sample);
            try { DurableJson.Write(Path.Combine(directory, "system-stats.json"), sample, backup: false); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log.Write("SYSTEM_WIDGET", "Unable to publish system readings to the wall."); }
            await Task.Delay(2000, token);
        }
    }, stoppingToken);
}
