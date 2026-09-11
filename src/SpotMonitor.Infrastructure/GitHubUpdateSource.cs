using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SpotMonitor.Core;

namespace SpotMonitor.Infrastructure;

public sealed class GitHubUpdateSource : IDisposable
{
    public const string DefaultRepository = "GalacticaActual75/RTSPView";
    private const long MaximumInstallerBytes = 512L * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly string _repository;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (DateTimeOffset At, GitHubUpdate? Release)> _cache = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public GitHubUpdateSource() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan },
        Environment.GetEnvironmentVariable("RTSPVIEW_GITHUB_REPOSITORY") ?? DefaultRepository) { }

    public GitHubUpdateSource(HttpClient http, string repository)
    {
        if (!Regex.IsMatch(repository, @"^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$"))
            throw new InvalidDataException("Configure a GitHub repository as owner/repository.");
        (_http, _repository) = (http, repository);
    }

    public string ChannelLocation(string channel) => $"https://github.com/{_repository}/releases" + (UpdateRelease.ValidateChannel(channel) == "stable" ? "/latest" : "");

    public async Task<GitHubUpdate?> FindAsync(string channel, bool refresh = false, CancellationToken cancellationToken = default)
    {
        UpdateRelease.ValidateChannel(channel);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _cache.TryGetValue(channel, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(2)) return cached.Release;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            Release? selected = null;
            if (channel == "stable")
            {
                try { selected = await ReadJsonAsync<Release>(Api("releases/latest"), 2 * 1024 * 1024, timeout.Token); }
                catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound) { throw new InvalidDataException("GitHub release unavailable. The configured repository must be public."); }
                if (selected.Draft || selected.Prerelease || ParseTag(selected.Tag).Channel != "stable") throw new InvalidDataException("The latest GitHub release is not a stable release.");
            }
            else
            {
                for (var page = 1; page <= 10; page++)
                {
                    var releases = await ReadJsonAsync<Release[]>(Api($"releases?per_page=100&page={page}"), 8 * 1024 * 1024, timeout.Token);
                    foreach (var release in releases.Where(r => !r.Draft && r.Prerelease))
                    {
                        UpdateRelease version;
                        try { version = ParseTag(release.Tag); } catch (InvalidDataException) { continue; }
                        if (version.Channel == "beta" && (selected is null || version.Number > ParseTag(selected.Tag).Number)) selected = release;
                    }
                    if (releases.Length < 100) break;
                    if (page == 10) throw new InvalidDataException("Too many releases to select a beta safely.");
                }
            }
            GitHubUpdate? result = null;
            if (selected is not null)
            {
                var version = ParseTag(selected.Tag);
                var manifestAsset = UniqueAsset(selected, "update.json");
                if (manifestAsset.Size is <= 0 or > 65536) throw new InvalidDataException("Invalid update manifest size.");
                var manifest = await ReadJsonAsync<UpdateManifest>(AssetUri(manifestAsset.Id), 65536, timeout.Token, asset: true);
                var expectedName = $"RTSPView-Setup-{version.Label}-win-x64.exe";
                if (manifest.Version != version.Label || version.Channel != channel || manifest.Installer != expectedName ||
                    !Regex.IsMatch(manifest.Sha256 ?? "", "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("Release manifest does not match the selected GitHub release.");
                var installer = UniqueAsset(selected, expectedName);
                if (installer.Size is <= 0 or > MaximumInstallerBytes) throw new InvalidDataException("Invalid installer size.");
                if (!string.IsNullOrEmpty(installer.Digest) && !installer.Digest.Equals("sha256:" + manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("GitHub's asset digest disagrees with the manifest.");
                result = new(manifest, installer.Id, installer.Size);
            }
            _cache[channel] = (DateTimeOffset.UtcNow, result);
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task DownloadAsync(GitHubUpdate release, string destination, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var response = await SendAsync(AssetUri(release.InstallerAssetId), true, timeout.Token);
            if (response.Content.Headers.ContentLength is { } length && length != release.InstallerSize) throw new InvalidDataException("Installer size changed.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[65536]; long received = 0; int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    received += count;
                    if (received > release.InstallerSize || received > MaximumInstallerBytes) throw new InvalidDataException("Installer exceeds the expected size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                }
                if (received != release.InstallerSize || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Manifest.Sha256)))
                    throw new InvalidDataException("Installer checksum or size verification failed.");
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<T> ReadJsonAsync<T>(Uri uri, int limit, CancellationToken token, bool asset = false)
    {
        using var response = await SendAsync(uri, asset, token);
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("GitHub response exceeds the allowed size.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("GitHub response exceeds the allowed size.");
            output.Write(buffer, 0, count);
        }
        // Existing release manifests may contain a UTF-8 BOM from Windows PowerShell.
        return JsonSerializer.Deserialize<T>(System.Text.Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF'), JsonOptions)
            ?? throw new InvalidDataException("Empty GitHub response.");
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, bool asset, CancellationToken token)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 ||
                uri.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("Untrusted GitHub download destination.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("RTSPView-Updater/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(asset ? "application/octet-stream" : "application/vnd.github+json"));
            if (uri.Host == "api.github.com")
            {
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            }
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location is null) throw new InvalidDataException("Missing GitHub download location.");
                uri = new Uri(uri, location); continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new InvalidDataException("Too many GitHub download redirects.");
    }

    private Uri Api(string path) => new($"https://api.github.com/repos/{_repository}/{path}");
    private Uri AssetUri(long id) => id > 0 ? Api($"releases/assets/{id}") : throw new InvalidDataException("Invalid release asset identifier.");
    private static UpdateRelease ParseTag(string tag) => tag.StartsWith('v') ? UpdateRelease.Parse(tag[1..]) : throw new InvalidDataException("Invalid release tag.");
    private static Asset UniqueAsset(Release release, string name)
    {
        var matches = release.Assets.Where(a => a.Name == name && a.State == "uploaded").ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException("The release is missing a unique uploaded asset.");
    }
    public void Dispose() { _http.Dispose(); _gate.Dispose(); }
    private sealed record Release([property: JsonPropertyName("tag_name")] string Tag, bool Draft, bool Prerelease, Asset[] Assets);
    private sealed record Asset(long Id, string Name, string State, long Size, string? Digest);
}

public sealed record UpdateManifest(string Version, string Installer, string Sha256, DateTimeOffset PublishedAt);
public sealed record GitHubUpdate(UpdateManifest Manifest, long InstallerAssetId, long InstallerSize);
