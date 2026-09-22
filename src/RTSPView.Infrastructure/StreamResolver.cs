using System.Diagnostics;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

// A lease owns the relay. Closing stdin also terminates it if the host exits unexpectedly.
public sealed class ResolvedStream : IDisposable
{
    private readonly Process? _process;
    private int _disposed;
    public Uri Uri { get; }
    public string Provider { get; }
    internal ResolvedStream(Uri uri, string provider, Process? process = null)
        => (Uri, Provider, _process) = (uri, provider, process);
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || _process is null) return;
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        try { _process.StandardInput.Close(); } catch (IOException) { }
        _process.Dispose();
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
        var executable = helperPath ?? HelperPath;
        if (!File.Exists(executable)) throw new InvalidOperationException("Streaming helper missing. Install the beta package with streaming support.");
        await Slots.WaitAsync(cancellationToken);
        Process? process = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
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
                url = settings.RtspUrl, mode = (int)settings.SourceMode, maximumHeight = settings.MaximumHeight
            }).AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (line is null || line.Length > 16384) throw new InvalidOperationException("Streaming helper stopped before opening the source.");
            using var response = JsonDocument.Parse(line);
            if (response.RootElement.TryGetProperty("error", out var error))
                throw new InvalidOperationException(error.GetString() ?? "Stream resolution failed.");
            var uri = new Uri(response.RootElement.GetProperty("url").GetString()!);
            if (uri.Scheme != "http" || uri.Host != "127.0.0.1") throw new InvalidOperationException("Invalid streaming helper response.");
            var result = new ResolvedStream(uri, response.RootElement.GetProperty("provider").GetString()!, process);
            process = null;
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException("Website stream resolution timed out. Check the source and retry."); }
        finally
        {
            if (process is not null) new ResolvedStream(new Uri("http://127.0.0.1"), "", process).Dispose();
            Slots.Release();
        }
    }
}
