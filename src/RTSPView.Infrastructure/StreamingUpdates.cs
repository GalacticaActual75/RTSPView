using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

public sealed record StreamingManifest(long Sequence, int Protocol, string Platform, string Url, string Sha256,
    long Size, string YtDlp, string Streamlink);
public sealed record StreamingUpdateState
{
    public string? Active { get; init; }
    public string? Previous { get; init; }
    public long HighestSequence { get; init; }
    public string? Rejected { get; init; }
    public DateTimeOffset? LastCheck { get; init; }
    public DateTimeOffset? InstalledAt { get; init; }
    public string Message { get; init; } = "Automatic streaming updates are enabled.";
}

// This updater runs only as the interactive user. The elevated maintenance service never executes these files.
public sealed class StreamingUpdates
{
    public const string Feed = "https://github.com/GalacticaActual75/RTSPView/releases/download/streaming-current/manifest.json";
    public static string BundledHelper => Path.Combine(AppContext.BaseDirectory, "Streaming", "stream-resolver.exe");
    public static StreamingUpdates Default { get; } = new(AppPaths.DataDirectory);
    private readonly string _root, _publicKey;
    private readonly Func<string, CancellationToken, Task> _probe;
    private string StatePath => Path.Combine(_root, "state.json");
    public StreamingUpdates(string dataDirectory, string? publicKey = null, Func<string, CancellationToken, Task>? probe = null)
    {
        _root = Path.Combine(dataDirectory, "Streaming");
        if (publicKey is null)
        {
            using var resource = typeof(StreamingUpdates).Assembly.GetManifestResourceStream("RTSPView.Infrastructure.streaming-update-public.txt")!;
            using var reader = new StreamReader(resource);
            publicKey = reader.ReadToEnd();
        }
        _publicKey = publicKey;
        _probe = probe ?? ProbeAsync;
    }
    public StreamingUpdateState Status()
    {
        try { return JsonSerializer.Deserialize<StreamingUpdateState>(File.ReadAllText(StatePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    private static bool IsId(string? id) => id is not null && Regex.IsMatch(id, "\\A[0-9A-F]{64}\\z");
    private string Package(string id) => Path.Combine(_root, id);
    private async Task<FileStream> LockAsync(CancellationToken token, string name = "selection.lock")
    {
        Directory.CreateDirectory(_root);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(_root, name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(100, token); }
        }
    }
    public StreamingManifest Verify(byte[] envelope)
    {
        if (envelope.Length > 32768) throw new InvalidDataException("Streaming manifest is too large.");
        using var doc = JsonDocument.Parse(envelope);
        var payload = Convert.FromBase64String(doc.RootElement.GetProperty("payload").GetString()!);
        var signature = Convert.FromBase64String(doc.RootElement.GetProperty("signature").GetString()!);
        using var rsa = RSA.Create(); rsa.ImportFromPem(_publicKey);
        if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new InvalidDataException("Streaming update signature is invalid.");
        var m = JsonSerializer.Deserialize<StreamingManifest>(payload) ?? throw new InvalidDataException();
        if (m.Protocol != 1 || m.Platform != "win-x64" || m.Sequence <= 0 || !IsId(m.Sha256) || m.Size is < 1 or > 350_000_000
            || string.IsNullOrWhiteSpace(m.YtDlp) || string.IsNullOrWhiteSpace(m.Streamlink))
            throw new InvalidDataException("Streaming update is incompatible.");
        var url = new Uri(m.Url);
        if (url.Scheme != "https" || url.Host != "github.com" || url.Port != 443 || url.UserInfo.Length != 0
            || !url.AbsolutePath.StartsWith("/GalacticaActual75/RTSPView/releases/download/streaming-", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid streaming package location.");
        return m;
    }
    private static async Task CopyBounded(Stream input, Stream output, long limit, CancellationToken token)
    {
        var buffer = new byte[65536]; long total = 0; int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            total += count;
            if (total > limit) throw new InvalidDataException("Streaming download exceeds its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }
    public async Task CheckAsync(HttpClient http, CancellationToken token)
    {
        using var gate = await LockAsync(token, "update.lock");
        StreamingUpdateState state;
        using (var selection = await LockAsync(token))
        {
            state = Status();
            if (state.LastCheck is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromDays(1)) return;
            state = state with { LastCheck = DateTimeOffset.UtcNow };
            DurableJson.Write(StatePath, state, false);
        }
        string? staging = null;
        try
        {
            using var response = await http.GetAsync(Feed, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            using var manifestBytes = new MemoryStream();
            await CopyBounded(await response.Content.ReadAsStreamAsync(token), manifestBytes, 32768, token);
            var m = Verify(manifestBytes.ToArray());
            if (m.Sequence < state.HighestSequence) throw new InvalidDataException("An older streaming update was refused.");
            if (m.Sha256 == state.Rejected) throw new InvalidDataException("This streaming package previously failed its health check.");
            if (m.Sha256 == state.Active)
            {
                using var currentGate = await LockAsync(token);
                var current = Status();
                if (current.Active == m.Sha256)
                    DurableJson.Write(StatePath, current with { Message = "Streaming components are up to date." }, false);
                return;
            }
            if (m.Sequence == state.HighestSequence && state.HighestSequence != 0)
                throw new InvalidDataException("Streaming release sequence was reused.");
            staging = Path.Combine(_root, "staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var archive = Path.Combine(staging, "package.zip");
            using (var download = await http.GetAsync(m.Url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                download.EnsureSuccessStatusCode();
                await using var file = File.Create(archive);
                await CopyBounded(await download.Content.ReadAsStreamAsync(token), file, m.Size, token);
                if (file.Length != m.Size) throw new InvalidDataException("Incomplete streaming package.");
            }
            await using (var file = File.OpenRead(archive))
                if (Convert.ToHexString(await SHA256.HashDataAsync(file, token)) != m.Sha256)
                    throw new InvalidDataException("Streaming package checksum is invalid.");
            var extracted = Path.Combine(staging, "files");
            Extract(archive, extracted);
            await _probe(Path.Combine(extracted, "stream-resolver.exe"), token);
            using var selection = await LockAsync(token);
            state = Status();
            var destination = Package(m.Sha256);
            // Recover a completed move interrupted before its active-pointer write.
            if (Directory.Exists(destination))
            {
                if (state.Active == m.Sha256 || state.Previous == m.Sha256)
                    throw new InvalidDataException("Streaming package is already retained.");
                using (new FileStream(Path.Combine(destination, ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
                Directory.Delete(destination, true);
            }
            File.WriteAllBytes(Path.Combine(extracted, "manifest.json"), manifestBytes.ToArray());
            Directory.Move(extracted, destination);
            DurableJson.Write(StatePath, state with
            {
                Active = m.Sha256, Previous = state.Active, HighestSequence = m.Sequence,
                InstalledAt = DateTimeOffset.UtcNow, Message = "Streaming components updated automatically. Active streams continue uninterrupted."
            }, false);
            Cleanup();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or IOException or UnauthorizedAccessException or JsonException or CryptographicException
            or ArgumentException or InvalidOperationException or FormatException or System.ComponentModel.Win32Exception or KeyNotFoundException)
        {
            // No upstream URLs, response bodies or exception text in status/logs.
            using var selection = await LockAsync(token);
            DurableJson.Write(StatePath, Status() with { Message = "Streaming update could not be verified or installed. The existing version remains available; retrying tomorrow." }, false);
        }
        finally
        {
            if (staging is not null)
                try { Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    public static void Extract(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archive);
        long total = 0;
        if (zip.Entries.Count > 20000) throw new InvalidDataException("Too many streaming package entries.");
        foreach (var entry in zip.Entries)
        {
            total += entry.Length;
            var parts = entry.FullName.Replace('\\', '/').Split('/');
            if (total > 1_000_000_000 || parts.Any(p => p is "." or ".." || p.Contains(':') || p.EndsWith(' ') || p.EndsWith('.'))
                || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException("Unsafe streaming package entry.");
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe streaming package path.");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, false);
        }
    }
    public async Task<(string Path, IDisposable? Lease)> SelectAsync(CancellationToken token)
    {
        using var gate = await LockAsync(token);
        var state = Status();
        foreach (var id in new[] { state.Active, state.Previous }.Distinct())
        {
            if (!IsId(id) || id == state.Rejected) continue;
            try
            {
                var folder = Package(id!);
                var m = Verify(File.ReadAllBytes(Path.Combine(folder, "manifest.json")));
                if (m.Sha256 != id) throw new InvalidDataException();
                var executable = Path.Combine(folder, "stream-resolver.exe");
                // A short local health check distinguishes broken packages from normal provider failures.
                await _probe(executable, token);
                var lease = new FileStream(Path.Combine(folder, ".lease"), FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);
                return (executable, lease);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException
                or CryptographicException or ArgumentException or FormatException or System.ComponentModel.Win32Exception or KeyNotFoundException)
            {
                state = state with { Active = state.Previous == id ? null : state.Previous, Previous = null, Rejected = id,
                    Message = "A streaming component failed its startup check. Automatically using the previous or bundled version." };
                DurableJson.Write(StatePath, state, false);
            }
        }
        return (BundledHelper, null);
    }
    private void Cleanup()
    {
        var state = Status();
        foreach (var path in Directory.EnumerateDirectories(_root))
        {
            var id = Path.GetFileName(path);
            if (!IsId(id) || id == state.Active || id == state.Previous) continue;
            try
            {
                using (new FileStream(Path.Combine(path, ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
                Directory.Delete(path, true);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    public object DisplayStatus()
    {
        var state = Status();
        try
        {
            var folder = IsId(state.Active) ? Package(state.Active!) : Path.GetDirectoryName(BundledHelper)!;
            using var info = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "versions.json")));
            return new { state, versions = info.RootElement.Clone() };
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        { return new { state, versions = new { } }; }
    }
    public static async Task ProbeAsync(string executable, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = new Process { StartInfo = new ProcessStartInfo(executable, "--self-test")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)! } };
        try
        {
            process.Start();
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            var output = await process.StandardOutput.ReadLineAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            using var result = JsonDocument.Parse(output ?? "{}");
            if (process.ExitCode != 0 || !result.RootElement.TryGetProperty("protocol", out var p) || p.GetInt32() != 1)
                throw new InvalidDataException("Streaming helper health check failed.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidDataException("Streaming helper health check timed out."); }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
        }
    }
}
