using System.Diagnostics;
using System.Globalization;

namespace RTSPView.Infrastructure;

public static class ViewerRestart
{
    public static ProcessStartInfo StartInfo(string executable, int processId, long startedAtUtcTicks)
    {
        var path = executable.Replace("'", "''");
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command" }) info.ArgumentList.Add(argument);
        // Bind to the original process, not a reused PID. Waiting for process exit also
        // waits for the Viewer's single-instance mutex to be released after disposal.
        info.ArgumentList.Add("$ErrorActionPreference = 'Stop'; " +
            "$previous = Get-Process -Id " + processId.ToString(CultureInfo.InvariantCulture) + " -ErrorAction SilentlyContinue; " +
            "if ($null -ne $previous) { try { if (!$previous.HasExited -and $previous.StartTime.ToUniversalTime().Ticks -eq " + startedAtUtcTicks.ToString(CultureInfo.InvariantCulture) +
            ") { $previous.WaitForExit() } } catch [System.InvalidOperationException] { if (!$previous.HasExited) { throw } } finally { $previous.Dispose() } }; " +
            "Start-Process -FilePath '" + path + "' -ArgumentList '--respect-viewer-pause'");
        return info;
    }
}
