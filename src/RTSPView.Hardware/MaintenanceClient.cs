using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using RTSPView.Core;

namespace RTSPView.Hardware;

public sealed class MaintenanceClient(string helperPath)
{
    public string HelperPath { get; } = Path.GetFullPath(helperPath);
    public async Task<MaintenanceStatus> SendAsync(MaintenanceRequest request, CancellationToken token = default)
    {
        using var connect = CancellationTokenSource.CreateLinkedTokenSource(token); connect.CancelAfter(1500);
        await using var pipe = new NamedPipeClientStream(".", MaintenanceProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(connect.Token);
        // Never trust a user process squatting on the helper's pipe name.
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId)) throw new IOException("Cannot identify maintenance helper.");
        using var process = OpenProcess(0x1000, false, processId);
        var path = new StringBuilder(32768); var size = path.Capacity;
        if (process.IsInvalid || !QueryFullProcessImageName(process, 0, path, ref size) || !path.ToString().Equals(HelperPath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Unexpected maintenance helper process.");
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), token);
        var response = new StringBuilder(); var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), token) != 0 && character[0] != '\n')
        { if (response.Length >= 16384) throw new InvalidDataException("Maintenance response too large."); response.Append(character[0]); }
        return JsonSerializer.Deserialize<MaintenanceStatus>(response.ToString()) ?? throw new InvalidDataException("Missing maintenance response.");
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);
}

public sealed class ServiceTemperatureSensors(MaintenanceClient client) : ITemperatureSensors
{
    private readonly HardwareTemperatureSensors _fallback = new();
    public (double? Cpu, double? Gpu) Read()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var status = client.SendAsync(new("status"), timeout.Token).GetAwaiter().GetResult();
            _fallback.Reset();
            var sample = status.Temperatures;
            return sample?.IsFresh(DateTimeOffset.UtcNow) == true ? (sample.CpuC, sample.GpuC) : (null, null);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException or JsonException)
        { return _fallback.Read(); }
    }
    public void Reset() => _fallback.Reset();
    public void Dispose() => _fallback.Dispose();
}
