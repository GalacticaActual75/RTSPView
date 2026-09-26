using RTSPView.Infrastructure;

namespace RTSPView.Controller;

public sealed class StreamingUpdateMonitor(RollingFileLogger logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => PluginRunner.RunAsync(RTSPView.Core.AppPaths.DataDirectory,
        p => p.YtDlp || p.Streamlink, RunEnabledAsync, stoppingToken);
    private async Task RunEnabledAsync(CancellationToken stoppingToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RTSPView-StreamingUpdater/1.0");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromMinutes(10));
                await StreamingUpdates.Default.CheckAsync(http, deadline.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            { logger.Write("STREAMING UPDATE", "Background component update failed; keeping the current helper and retrying later."); }
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }
}
