using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

internal static class PluginRunner
{
    // Cancel polling and metadata requests when the saved switch changes, including external imports.
    public static async Task RunAsync(string directory, Func<Plugins, bool> enabled,
        Func<CancellationToken, Task> run, CancellationToken stopping)
    {
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
        CancellationTokenSource? active = null;
        Task? worker = null;
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                bool on;
                try { on = enabled((await store.LoadAsync(stopping)).Plugins); }
                catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
                { await Task.Delay(1000, stopping); continue; }
                if (on && active is null)
                {
                    active = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                    worker = run(active.Token);
                }
                else if (!on && active is not null)
                {
                    active.Cancel();
                    try { await worker!; } catch (OperationCanceledException) { }
                    active.Dispose(); active = null; worker = null;
                }
                if (worker is { IsCompleted: true }) await worker;
                await Task.Delay(1000, stopping);
            }
        }
        finally
        {
            if (active is not null)
            {
                active.Cancel();
                try { await worker!; } catch (OperationCanceledException) { }
                active.Dispose();
            }
        }
    }
}
