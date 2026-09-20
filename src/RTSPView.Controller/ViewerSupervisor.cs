using System.Diagnostics;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

public sealed class ViewerSupervisor(ViewerTelemetryClient telemetry, ViewerCommandClient commands, RollingFileLogger logger, PawnIoInstaller dependencies, ViewerRuntimeState runtime, ViewerLauncher launcher) : BackgroundService
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastRecoveryAttempt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            if (runtime.Paused || launcher.Starting) continue;
            if (dependencies.Busy || (await dependencies.StatusAsync()).State is "installing" or "downloading" or "update-installing") continue;
            var now = DateTimeOffset.UtcNow;
            if (now - _startedAt < TimeSpan.FromSeconds(45) || now - _lastRecoveryAttempt < TimeSpan.FromMinutes(1)) continue;
            var lastSeen = telemetry.LastReceivedAt ?? _startedAt;
            if (now - lastSeen < TimeSpan.FromSeconds(25)) continue;
            using var gate = await runtime.AcquireAsync(stoppingToken);
            if (runtime.Paused || launcher.Starting) continue;
            _lastRecoveryAttempt = now;
            logger.Write("WATCHDOG", $"Viewer telemetry missing for {(now - lastSeen).TotalSeconds:0} seconds; recovery started");

            var result = await commands.SendAsync(RTSPView.Core.ViewerCommandType.RestartViewer, null, stoppingToken);
            if (result.ExitingIntentionally)
            {
                logger.Write("WATCHDOG", "Viewer is intentionally exiting; recovery canceled");
                continue;
            }
            if (result.Success)
            {
                logger.Write("WATCHDOG", "Viewer accepted supervised restart command");
                continue;
            }

            try
            {
                var viewerPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Viewer", "SpotMonitor.Viewer.exe"));
                foreach (var process in Process.GetProcessesByName("SpotMonitor.Viewer"))
                {
                    try
                    {
                        if (string.Equals(process.MainModule?.FileName, viewerPath, StringComparison.OrdinalIgnoreCase)) process.Kill(true);
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
                if (!File.Exists(viewerPath)) throw new FileNotFoundException("Viewer executable not found.", viewerPath);
                Process.Start(new ProcessStartInfo(viewerPath, "--respect-viewer-pause") { UseShellExecute = true });
                logger.Write("WATCHDOG", "Viewer process relaunched after IPC recovery failed");
            }
            catch (Exception exception) { logger.Write("ERROR", $"Viewer supervision failed: {exception.Message}"); }
        }
    }
}
