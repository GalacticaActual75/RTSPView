using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using RTSPView.Core;
using RTSPView.Hardware;

namespace RTSPView.Controller;

public sealed class PawnIoInstaller(MaintenanceClient client, TemperatureMonitor temperatures, UpdateService updates, bool hostActionsAllowed)
{
    private int _busy;
    private MaintenanceStatus? _local;
    public bool Busy => Volatile.Read(ref _busy) != 0;
    public async Task<MaintenanceStatus> RemoteStatusAsync()
    {
        if (!hostActionsAllowed) return new();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { return await client.SendAsync(new("status"), timeout.Token); }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public async Task<MaintenanceStatus> StatusAsync()
    {
        if (!hostActionsAllowed) return new();
        if (_local is { State: "enabling" or "starting" }) return _local;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var remote = await client.SendAsync(new("status"), timeout.Token);
            return _local is { State: "failed" } ? _local with { Available = remote.Available, PawnInstalled = remote.PawnInstalled } : remote;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException or JsonException)
        { return _local ?? ConnectionFailure(error); }
    }
    public static MaintenanceStatus ConnectionFailure(Exception error) => new()
    {
        State = "unavailable",
        Message = error switch
        {
            OperationCanceledException => "The maintenance helper is not responding. Check the RTSPViewMaintenance service on the host; setup may not have completed or the service may have stopped.",
            UnauthorizedAccessException => "The Controller could not authenticate the maintenance helper connection. " + error.Message,
            _ => "The maintenance helper connection failed: " + error.Message
        }
    };
    public Task<bool> StartAsync(bool enable)
    {
        if (!hostActionsAllowed || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return Task.FromResult(false);
        _local = new() { State = enable ? "enabling" : "starting", Message = enable ? "Waiting for Windows approval on the host." : "Preparing PawnIO installation." };
        _ = Task.Run(async () =>
        {
            try
            {
                var ran = await updates.TryRunMaintenanceAsync(async () =>
                {
                    if (enable) await EnableAsync();
                    else await InstallAsync();
                }, CancellationToken.None);
                if (!ran) throw new InvalidOperationException("An RTSPView update or scheduled maintenance is active. Try again when it finishes.");
            }
            catch (Exception error) { _local = new() { State = "failed", Message = error is Win32Exception { NativeErrorCode: 1223 } ? "Windows approval was cancelled." : error.Message }; }
            finally { Interlocked.Exchange(ref _busy, 0); }
        });
        return Task.FromResult(true);
    }
    private async Task EnableAsync()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\') + "\\";
        if (!client.HelperPath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) || !File.Exists(client.HelperPath))
            throw new InvalidOperationException("Install the RTSPView build containing Maintenance under Program Files first.");
        var start = new ProcessStartInfo(client.HelperPath) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--install"); start.ArgumentList.Add(WindowsIdentity.GetCurrent().User!.Value);
        using var process = Process.Start(start) ?? throw new IOException("Windows could not start maintenance setup.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new IOException("Maintenance setup failed. Check administrator approval and that RTSPView is installed in an administrator-protected Program Files folder.");
        // SCM accepting 'start' does not prove that the service's pipe is ready.
        Exception? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var status = await client.SendAsync(new("status"), timeout.Token);
                if (status.Available) { _local = null; return; }
                lastError = new IOException(status.Message);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException or JsonException) { lastError = error; }
            await Task.Delay(500);
        }
        throw new IOException("Windows setup exited successfully, but the helper did not become ready. " + ConnectionFailure(lastError ?? new IOException("No ready response.")).Message);
    }
    private async Task InstallAsync()
    {
        _local = null;
        using var downloadTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        var ready = await client.SendAsync(new("prepare"), downloadTimeout.Token);
        if (ready.State == "complete") { _local = null; return; }
        if (ready.State != "ready" || ready.OperationId is null) throw new IOException(ready.Message);
        try
        {
            await temperatures.SuspendAsync(true);
            // The helper owns the transaction even if this HTTP request/browser disconnects.
            _local = null;
            MaintenanceStatus result;
            try { result = await client.SendAsync(new("install", ready.OperationId)); }
            catch (IOException)
            {
                // A pipe disconnect does not cancel a privileged installer. Keep sensor reads paused
                // while the helper still reports installation, rather than racing driver setup.
                do { await Task.Delay(2000); result = await RemoteStatusAsync(); } while (result.State == "installing");
                if (!result.Available) throw new IOException("The helper disconnected. Check PawnIO installation status on the host before retrying.");
            }
            if (result.State != "complete") throw new IOException(result.Message);
        }
        finally
        {
            await temperatures.SuspendAsync(false);
        }
    }
}
