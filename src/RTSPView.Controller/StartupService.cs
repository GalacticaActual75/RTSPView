using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;

namespace RTSPView.Controller;

public sealed record StartupStatus(bool Enabled, bool Managed, bool RepairNeeded, string Message);
public sealed record StartupRequest(bool Enabled);

public sealed class StartupService(bool testing = false, Func<Task<StartupStatus>>? read = null, Func<bool, Task>? configure = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static string Helper => Path.Combine(AppContext.BaseDirectory, "Configure-Startup.ps1");
    public async Task<StartupStatus> StatusAsync()
    {
        if (read is not null) return await read();
        if (testing || !File.Exists(Helper)) return new(false, false, false, "Windows startup is managed by the installed application. Install this beta to configure it on the host.");
        var start = StartInfo("Status"); start.UseShellExecute = false; start.CreateNoWindow = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows startup status could not be read.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw new InvalidOperationException("Windows startup status timed out. Try again."); }
        await error;
        if (process.ExitCode != 0) throw new InvalidOperationException("Windows startup status could not be read. Check Task Scheduler on the host.");
        return JsonSerializer.Deserialize<StartupStatus>(await output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Windows returned no startup status.");
    }
    public async Task<StartupStatus> SetAsync(bool enabled)
    {
        if (!await _gate.WaitAsync(0)) throw new InvalidOperationException("A Windows startup change is already in progress.");
        try
        {
            if (!(await StatusAsync()).Managed) throw new InvalidOperationException("Windows startup requires an installed application on the host.");
            if (configure is not null) await configure(enabled);
            else
            {
                var start = StartInfo(enabled ? "Enable" : "Disable"); start.UseShellExecute = true; start.Verb = "runas";
                try
                {
                    using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start startup setup.");
                    await process.WaitForExitAsync();
                    if (process.ExitCode != 0) throw new InvalidOperationException("Windows could not update startup. Check the host's Task Scheduler and try again.");
                }
                catch (Win32Exception e) when (e.NativeErrorCode == 1223) { throw new InvalidOperationException("Windows approval was cancelled. The startup setting was not changed."); }
            }
            var status = await StatusAsync();
            if (status.Enabled != enabled || (enabled && status.RepairNeeded)) throw new InvalidOperationException("Windows startup could not be verified. Refresh its status and try again.");
            return status;
        }
        finally { _gate.Release(); }
    }
    private static ProcessStartInfo StartInfo(string mode)
    {
        var info = new ProcessStartInfo("powershell.exe") { WindowStyle = ProcessWindowStyle.Hidden };
        // Capture the interactive application's SID before elevation, not the UAC administrator's account.
        using var identity = WindowsIdentity.GetCurrent();
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", Helper, "-Mode", mode, "-UserSid", identity.User!.Value }) info.ArgumentList.Add(arg);
        return info;
    }
}
