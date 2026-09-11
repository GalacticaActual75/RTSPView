using System.Diagnostics;
using System.IO.Pipes;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Controller;

public sealed class ViewerTelemetryClient : BackgroundService
{
    public const string PipeName = "RTSPView.Telemetry.v1";
    private ViewerTelemetry? _latest;
    public DateTimeOffset? LastReceivedAt { get; private set; }
    public ViewerTelemetry? Latest => _latest is { } value && DateTimeOffset.UtcNow - value.Timestamp < TimeSpan.FromSeconds(5) ? value : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.In, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(2000, stoppingToken);
                using var reader = new StreamReader(pipe);
                while (!stoppingToken.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(stoppingToken);
                    if (line is null) break;
                    var snapshot = JsonSerializer.Deserialize<ViewerTelemetry>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (snapshot is not null) { _latest = snapshot; LastReceivedAt = DateTimeOffset.UtcNow; }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { await Task.Delay(1000, stoppingToken); }
        }
    }
}

public sealed class SystemMetricsCollector : IDisposable
{
    private readonly PerformanceCounter? _cpu;
    private readonly List<PerformanceCounter> _gpu = [];
    private readonly List<PerformanceCounter> _videoDecode = [];
    private HashSet<string> _gpuInstances = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastGpuDiscovery = DateTimeOffset.MinValue;
    private long _lastReceived;
    private long _lastSent;
    private DateTimeOffset _lastNetworkSample = DateTimeOffset.UtcNow;

    public SystemMetricsCollector()
    {
        try { _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", true); _cpu.NextValue(); } catch { }
        RefreshGpuCounters(force: true);
        (_lastReceived, _lastSent) = ReadNetworkBytes();
    }

    public SystemTelemetry GetSnapshot()
    {
        RefreshGpuCounters();
        var now = DateTimeOffset.UtcNow;
        var (received, sent) = ReadNetworkBytes();
        var seconds = Math.Max(0.1, (now - _lastNetworkSample).TotalSeconds);
        var snapshot = new SystemTelemetry
        {
            Timestamp = now,
            CpuPercent = ReadCounter(_cpu),
            GpuPercent = ReadMax(_gpu.Concat(_videoDecode)),
            GpuVideoDecodePercent = ReadMax(_videoDecode),
            NetworkReceiveMbps = Math.Max(0, (received - _lastReceived) * 8 / seconds / 1_000_000d),
            NetworkSendMbps = Math.Max(0, (sent - _lastSent) * 8 / seconds / 1_000_000d)
        };
        if (TryReadMemory(out var usedGb, out var totalGb, out var percent)) snapshot = snapshot with { RamUsedGb = usedGb, RamTotalGb = totalGb, RamPercent = percent };
        _lastReceived = received; _lastSent = sent; _lastNetworkSample = now;
        return snapshot;
    }

    private void RefreshGpuCounters(bool force = false)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastGpuDiscovery < TimeSpan.FromSeconds(5)) return;
        _lastGpuDiscovery = now;
        try
        {
            var instances = new PerformanceCounterCategory("GPU Engine").GetInstanceNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!force && instances.SetEquals(_gpuInstances)) return;
            DisposeCounters(_gpu); DisposeCounters(_videoDecode); _gpu.Clear(); _videoDecode.Clear();
            foreach (var instance in instances)
            {
                var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                if (instance.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase)) _videoDecode.Add(counter);
                else _gpu.Add(counter);
                counter.NextValue();
            }
            _gpuInstances = instances;
        }
        catch
        {
            DisposeCounters(_gpu); DisposeCounters(_videoDecode); _gpu.Clear(); _videoDecode.Clear();
            _gpuInstances.Clear();
        }
    }

    private static double? ReadCounter(PerformanceCounter? counter) { try { return counter is null ? null : Math.Round(Math.Clamp(counter.NextValue(), 0, 100), 1); } catch { return null; } }
    private static double? ReadMax(IEnumerable<PerformanceCounter> counters)
    {
        var values = counters.Select(ReadCounter).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return values.Length == 0 ? null : Math.Round(values.Max(), 1);
    }
    private static (long Received, long Sent) ReadNetworkBytes()
    {
        long received = 0, sent = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(item => item.OperationalStatus == OperationalStatus.Up && item.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            try { var stats = adapter.GetIPv4Statistics(); received += stats.BytesReceived; sent += stats.BytesSent; } catch { }
        return (received, sent);
    }
    private static bool TryReadMemory(out double usedGb, out double totalGb, out double percent)
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) { usedGb = totalGb = percent = 0; return false; }
        totalGb = Math.Round(status.TotalPhysical / 1024d / 1024d / 1024d, 1);
        usedGb = Math.Round((status.TotalPhysical - status.AvailablePhysical) / 1024d / 1024d / 1024d, 1);
        percent = Math.Round((double)status.MemoryLoad, 1);
        return true;
    }
    public void Dispose() { _cpu?.Dispose(); DisposeCounters(_gpu); DisposeCounters(_videoDecode); }
    private static void DisposeCounters(IEnumerable<PerformanceCounter> counters) { foreach (var counter in counters) counter.Dispose(); }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>(); public uint MemoryLoad; public ulong TotalPhysical; public ulong AvailablePhysical; public ulong TotalPageFile; public ulong AvailablePageFile; public ulong TotalVirtual; public ulong AvailableVirtual; public ulong AvailableExtendedVirtual;
    }
}
