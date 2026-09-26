using System.Diagnostics;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

// A lease owns the relay. Closing stdin also terminates it if the host exits unexpectedly.
public sealed class ResolvedStream : IDisposable
{
    private readonly Process? _process;
    private readonly IDisposable? _packageLease;
    private int _disposed;
    public Uri Uri { get; }
    public string Provider { get; }
    internal ResolvedStream(Uri uri, string provider, Process? process = null, IDisposable? packageLease = null)
        => (Uri, Provider, _process, _packageLease) = (uri, provider, process, packageLease);
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_process is null) { _packageLease?.Dispose(); return; }
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        try { _process.StandardInput.Close(); } catch (IOException) { }
        _process.Dispose();
        _packageLease?.Dispose();
    }
}

public static class StreamResolver
{
    private static readonly SemaphoreSlim Slots = new(3);
    public static string HelperPath => Path.Combine(AppContext.BaseDirectory, "Streaming", "stream-resolver.exe");

    public static async Task<ResolvedStream> ResolveAsync(CameraSettings settings, CancellationToken cancellationToken,
        string? helperPath = null)
    {
        StreamSource.Validate(settings);
        if (!StreamSource.NeedsResolver(settings)) return new(new Uri(settings.RtspUrl), "Direct");
        var plugins = (await new JsonSettingsStore(Path.Combine(AppPaths.DataDirectory, "settings.json")).LoadAsync(cancellationToken)).Plugins;
        var mode = settings.SourceMode;
        if (mode == StreamSourceMode.Streamlink && !plugins.Streamlink || mode == StreamSourceMode.YtDlp && !plugins.YtDlp || !plugins.Streamlink && !plugins.YtDlp)
            throw new InvalidOperationException("The required source plugin is disabled.");
        if (mode == StreamSourceMode.Auto && !plugins.Streamlink) mode = StreamSourceMode.YtDlp;
        if (mode == StreamSourceMode.Auto && !plugins.YtDlp) mode = StreamSourceMode.Streamlink;
        IDisposable? packageLease = null;
        Process? process = null;
        var acquired = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            // Queued requests need the same deadline as active helpers.
            // Otherwise three slow sources can leave a fourth waiting indefinitely.
            await Slots.WaitAsync(timeout.Token);
            acquired = true;
            var selected = helperPath is null ? await StreamingUpdates.Default.SelectAsync(timeout.Token) : (helperPath, (IDisposable?)null);
            var executable = selected.Item1;
            packageLease = selected.Item2;
            if (!File.Exists(executable)) throw new InvalidOperationException("Streaming helper missing. Reinstall RTSPView with streaming support.");
            process = new Process { StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            }};
            process.Start();
            // Never put source URLs or credentials in a process command line or logs.
            _ = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                url = settings.RtspUrl, mode = (int)mode, maximumHeight = settings.MaximumHeight
            }).AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (line is null || line.Length > 16384) throw new InvalidOperationException("Streaming helper stopped before opening the source.");
            using var response = JsonDocument.Parse(line);
            if (response.RootElement.TryGetProperty("error", out var error))
                throw new InvalidOperationException(error.GetString() ?? "Stream resolution failed.");
            var uri = new Uri(response.RootElement.GetProperty("url").GetString()!);
            if (uri.Scheme != "http" || uri.Host != "127.0.0.1") throw new InvalidOperationException("Invalid streaming helper response.");
            var result = new ResolvedStream(uri, response.RootElement.GetProperty("provider").GetString()!, process, packageLease);
            process = null;
            packageLease = null;
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException("Website stream resolution timed out. Check the source and retry."); }
        finally
        {
            if (process is not null) new ResolvedStream(new Uri("http://127.0.0.1"), "", process).Dispose();
            packageLease?.Dispose();
            if (acquired) Slots.Release();
        }
    }
}
