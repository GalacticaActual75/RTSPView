using System.Diagnostics;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

// A short-lived copy of the controller acts as the handoff, outside the host lifetime.
public static class ApplicationRestart
{
    public const string HelperSwitch = "--restart-application-helper";

    public static async Task CompleteAsync(Func<Task> waitForController, Func<Task> stopViewer, Action resumeRecovery, Action startController, Action startViewer)
    {
        await waitForController();
        // Restore administration and recovery even if the viewer cannot be stopped.
        try { await stopViewer(); }
        finally { resumeRecovery(); startController(); }
        startViewer();
    }

    public static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    public static void Prepare(string[] arguments)
    {
        var executable = Environment.ProcessPath!;
        var viewer = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Viewer", "SpotMonitor.Viewer.exe"));
        if (!Path.GetFileName(executable).Equals("SpotMonitor.Controller.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(viewer))
            throw new InvalidOperationException("Application restart requires an installed RTSPView application.");
        using var parent = Process.GetCurrentProcess();
        var eventName = "Local\\RTSPView.Restart." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        using var helper = Process.Start(StartInfo(executable, new[] { HelperSwitch, parent.Id.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString(), eventName }.Concat(arguments)))
            ?? throw new IOException("Unable to start the restart helper.");
        if (!ready.WaitOne(TimeSpan.FromSeconds(10)))
        {
            if (!helper.HasExited) helper.Kill();
            throw new IOException("Restart helper did not become ready.");
        }
    }

    public static async Task RunHelperAsync(string[] arguments)
    {
        try
        {
            if (arguments.Length < 4) return;
            using var parent = Process.GetProcessById(int.Parse(arguments[1]));
            if (parent.StartTime.ToUniversalTime().Ticks != long.Parse(arguments[2]) ||
                !string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) ||
                parent.SessionId != Process.GetCurrentProcess().SessionId) return;
            using var ready = EventWaitHandle.OpenExisting(arguments[3]);
            ready.Set();
            // Never terminate a controller that failed to acknowledge shutdown.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var viewerPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Viewer", "SpotMonitor.Viewer.exe"));
            var runtime = new ViewerRuntimeState(AppPaths.DataDirectory);
            await CompleteAsync(() => parent.WaitForExitAsync(timeout.Token), async () =>
            {
                using var gate = await runtime.AcquireAsync(timeout.Token);
                foreach (var viewer in Process.GetProcessesByName("SpotMonitor.Viewer"))
                {
                    using (viewer)
                    {
                        if (viewer.SessionId != Process.GetCurrentProcess().SessionId ||
                            !string.Equals(viewer.MainModule?.FileName, viewerPath, StringComparison.OrdinalIgnoreCase)) continue;
                        viewer.CloseMainWindow();
                        if (!viewer.WaitForExit(5000)) { viewer.Kill(true); await viewer.WaitForExitAsync(timeout.Token); }
                    }
                }
            }, runtime.Resume,
                () => { using var controller = Process.Start(StartInfo(Environment.ProcessPath!, arguments.Skip(4))) ?? throw new IOException("Controller did not start."); },
                () => { using var liveView = Process.Start(StartInfo(viewerPath, Array.Empty<string>())) ?? throw new IOException("Viewer did not start."); });
        }
        catch (Exception error)
        {
            new RollingFileLogger(Path.Combine(AppPaths.DataDirectory, "logs")).Write("ERROR", "Application restart handoff failed: " + error.GetType().Name);
        }
    }
}
