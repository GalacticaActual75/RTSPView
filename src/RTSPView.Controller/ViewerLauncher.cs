using System.Diagnostics;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

public sealed class ViewerLauncher
{
    private readonly ViewerRuntimeState state;
    private readonly Func<bool> isRunning;
    private readonly Action start;
    private readonly TimeProvider clock;
    private long _lastStartTicks;
    public ViewerLauncher(ViewerRuntimeState state) : this(state, ViewerRuntimeState.IsRunning, LaunchInstalledViewer) { }
    public ViewerLauncher(ViewerRuntimeState state, Func<bool> isRunning, Action start, TimeProvider? clock = null)
        => (this.state, this.isRunning, this.start, this.clock) = (state, isRunning, start, clock ?? TimeProvider.System);
    public bool Starting => !state.Paused && clock.GetUtcNow().UtcTicks - Interlocked.Read(ref _lastStartTicks) < TimeSpan.FromSeconds(30).Ticks && !isRunning();
    public async Task<string> StartAsync(CancellationToken token)
    {
        using var gate = await state.AcquireAsync(token);
        if (isRunning()) return "Viewer is already running.";
        if (Starting) return "Viewer is already starting.";
        start();
        Interlocked.Exchange(ref _lastStartTicks, clock.GetUtcNow().UtcTicks);
        state.Resume();
        return "Viewer starting. Automatic recovery is enabled.";
    }

    private static void LaunchInstalledViewer()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Viewer", "SpotMonitor.Viewer.exe"));
        if (!File.Exists(path)) throw new FileNotFoundException("The installed viewer executable was not found.");
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })
            ?? throw new IOException("Windows could not start the viewer.");
    }
}
