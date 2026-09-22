using System.Diagnostics;
using RTSPView.Infrastructure;

internal static class ViewerRestartChecks
{
    public static async Task Run()
    {
        var marker = Path.Combine(Path.GetTempPath(), "RTSPView-restart-" + Guid.NewGuid().ToString("N"));
        var executable = Environment.ProcessPath!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--restart-fixture-parent");
        start.Environment["RTSPVIEW_RESTART_TEST_MARKER"] = marker;
        using var previous = Process.Start(start)!;
        Process? helper = null;
        try
        {
            while (!File.Exists(marker + ".ready")) await Task.Delay(25, timeout.Token);
            var info = ViewerRestart.StartInfo(executable, previous.Id, previous.StartTime.ToUniversalTime().Ticks);
            info.Environment["RTSPVIEW_RESTART_TEST_MARKER"] = marker;
            helper = Process.Start(info)!;
            await Task.Delay(2500, timeout.Token);
            if (previous.HasExited || File.Exists(marker)) throw new Exception("Restart failed to wait for the previous Viewer.");
            await previous.WaitForExitAsync(timeout.Token);
            await helper.WaitForExitAsync(timeout.Token);
            while (!File.Exists(marker)) await Task.Delay(25, timeout.Token);
            if (helper.ExitCode != 0 || File.ReadAllLines(marker).Length != 1) throw new Exception("Restart did not launch exactly one replacement after mutex release.");
            helper.Dispose();
            // Fast shutdown: the old PID can already be gone when the helper starts.
            helper = Process.Start(info)!;
            await helper.WaitForExitAsync(timeout.Token);
            while (File.ReadAllLines(marker).Length < 2) await Task.Delay(25, timeout.Token);
            if (helper.ExitCode != 0 || File.ReadAllLines(marker).Length != 2) throw new Exception("Restart of an already-exited process failed.");
            Console.WriteLine("PASS real restart helper waits through a four-second shutdown, respects mutex release, and handles an already-exited parent");
        }
        finally
        {
            if (!previous.HasExited) previous.Kill(true);
            if (helper is not null) { if (!helper.HasExited) helper.Kill(true); helper.Dispose(); }
            File.Delete(marker); File.Delete(marker + ".ready");
        }
    }
}
