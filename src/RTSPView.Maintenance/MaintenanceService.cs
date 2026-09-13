using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;
using RTSPView.Core;
using RTSPView.Hardware;

namespace RTSPView.Maintenance;

public sealed class MaintenanceService : ServiceBase
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sensorGate = new(1, 1);
    private readonly HardwareTemperatureSensors _sensors = new();
    private readonly string _stateDirectory = Path.Combine(AppContext.BaseDirectory, "State");
    private TemperatureStatus _temperatures = new();
    private MaintenanceEngine? _engine;
    private Task? _worker, _sampler;
    private string _allowedSid = "";
    private string _installer = "";
    public MaintenanceService() { ServiceName = MaintenanceProtocol.ServiceName; CanShutdown = false; }

    protected override void OnStart(string[] args)
    {
        HelperSetup.ValidateProtectedPath(Environment.ProcessPath!);
        using var key = Registry.LocalMachine.OpenSubKey(MaintenanceProtocol.RegistryPath);
        _allowedSid = new SecurityIdentifier((string?)key?.GetValue("AllowedSid") ?? throw new InvalidOperationException("No authorized user configured.")).Value;
        Directory.CreateDirectory(_stateDirectory);
        HelperSetup.ValidateProtectedPath(_stateDirectory);
        _installer = Path.Combine(_stateDirectory, "PawnIO-" + MaintenanceProtocol.PawnVersion + ".exe");
        foreach (var process in Process.GetProcessesByName("PawnIO-" + MaintenanceProtocol.PawnVersion))
        {
            using (process)
                if (string.Equals(process.MainModule?.FileName, _installer, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A previous PawnIO installer is still running. Wait for it to finish before starting maintenance.");
        }
        var statusPath = Path.Combine(_stateDirectory, "status.json");
        var previous = File.Exists(statusPath) ? JsonSerializer.Deserialize<MaintenanceStatus>(File.ReadAllText(statusPath)) : null;
        _engine = new MaintenanceEngine(PawnInstalled, PrepareAsync, InstallAsync, Persist, previous);
        // An interrupted operation is never automatically replayed after a service restart.
        _worker = Task.Run(ServeAsync);
        _sampler = Task.Run(SampleAsync);
    }

    private static bool PawnInstalled()
    {
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        return service is not null;
    }
    private void Persist(MaintenanceStatus status)
    {
        var path = Path.Combine(_stateDirectory, "status.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(status));
        File.Move(path + ".tmp", path, true);
    }
    private async Task PrepareAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RTSPView-Maintenance/1.0");
        using var response = await client.GetAsync(MaintenanceProtocol.PawnUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 64 * 1024 * 1024) throw new InvalidDataException("Installer too large.");
        var temporary = Path.Combine(_stateDirectory, Guid.NewGuid() + ".download");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            {
                var buffer = new byte[81920]; long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token)) != 0)
                {
                    total += count; if (total > 64 * 1024 * 1024) throw new InvalidDataException("Installer too large.");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                }
            }
            await VerifyAsync(temporary);
            File.Move(temporary, _installer, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task VerifyAsync(string path)
    {
        HelperSetup.ValidateProtectedPath(path);
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        if (hash != MaintenanceProtocol.PawnSha256) throw new InvalidDataException("PawnIO checksum mismatch.");
    }
    private async Task<int> InstallAsync()
    {
        await VerifyAsync(_installer);
        await _sensorGate.WaitAsync();
        try
        {
            _sensors.Reset(); Volatile.Write(ref _temperatures, new());
            var start = new ProcessStartInfo(_installer) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _stateDirectory };
            start.ArgumentList.Add("-install"); start.ArgumentList.Add("-silent");
            using var installer = Process.Start(start) ?? throw new IOException("Installer could not start.");
            // Keep the operation active until the installer exits; never claim success on a timeout.
            await installer.WaitForExitAsync();
            return installer.ExitCode;
        }
        finally { _sensorGate.Release(); }
    }
    private async Task SampleAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            if (await _sensorGate.WaitAsync(0))
            {
                try
                {
                    var sample = _sensors.Read();
                    Volatile.Write(ref _temperatures, new() { Timestamp = DateTimeOffset.UtcNow, CpuC = sample.Cpu, GpuC = sample.Gpu });
                }
                catch { Volatile.Write(ref _temperatures, new()); }
                finally { _sensorGate.Release(); }
            }
            await Task.Delay(5000, _stop.Token);
        }
    }
    private async Task ServeAsync()
    {
        var first = true;
        var clients = new List<Task>();
        while (!_stop.IsCancellationRequested)
        {
            clients.RemoveAll(task => task.IsCompleted);
            if (clients.Count >= 8) { await Task.WhenAny(clients); continue; }
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(_allowedSid), PipeAccessRights.ReadWrite, AccessControlType.Allow));
            var pipe = NamedPipeServerStreamAcl.Create(MaintenanceProtocol.PipeName, PipeDirection.InOut, 10, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : 0), 4096, 4096, security);
            first = false;
            try { await pipe.WaitForConnectionAsync(_stop.Token); clients.Add(HandleAsync(pipe)); }
            catch { pipe.Dispose(); throw; }
        }
    }
    private async Task HandleAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(pipe, leaveOpen: true);
                var text = new System.Text.StringBuilder(); var character = new char[1];
                while (await reader.ReadAsync(character.AsMemory(), timeout.Token) != 0 && character[0] != '\n')
                { if (text.Length >= 1024) throw new InvalidDataException("Request too long."); text.Append(character[0]); }
                string? sid = null;
                pipe.RunAsClient(() =>
                {
                    using var identity = WindowsIdentity.GetCurrent(true);
                    sid = identity?.User?.Value;
                });
                if (sid != _allowedSid) throw new UnauthorizedAccessException("The connecting Windows account does not match the account authorized during helper setup. Enable the helper from the Controller's Windows account.");
                var request = JsonSerializer.Deserialize<MaintenanceRequest>(text.ToString()) ?? throw new InvalidDataException();
                var status = await _engine!.HandleAsync(request);
                var sample = Volatile.Read(ref _temperatures);
                status = status with { Temperatures = sample.IsFresh(DateTimeOffset.UtcNow) ? sample : null };
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                using var writeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await writer.WriteLineAsync(JsonSerializer.Serialize(status).AsMemory(), writeTimeout.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or ArgumentException or UnauthorizedAccessException)
            {
                // Never turn a rejected request into an empty JSON response. No privileged
                // action is performed here, and unauthorized clients receive no sensor data.
                var message = error is OperationCanceledException ? "The helper timed out while reading the Controller request."
                    : "The helper rejected the request: " + error.Message;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new MaintenanceStatus { State = "failed", Message = message }).AsMemory(), timeout.Token);
                }
                catch (Exception replyError) when (replyError is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
        }
    }
    protected override void OnStop()
    {
        if (_engine is not null && !_engine.TryBeginStop()) throw new InvalidOperationException("Wait for the active PawnIO operation to finish before stopping maintenance.");
        _stop.Cancel();
        try { Task.WhenAll(_worker ?? Task.CompletedTask, _sampler ?? Task.CompletedTask).Wait(TimeSpan.FromSeconds(10)); } catch (AggregateException) { }
        _sensors.Dispose();
    }
}
