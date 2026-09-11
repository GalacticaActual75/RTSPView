using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpotMonitor.Infrastructure;

internal static class GitHubUpdateChecks
{
    public static async Task RunAsync(string root)
    {
        var bytes = Encoding.UTF8.GetBytes("Synthetic installer bytes; never executed.");
        var checksum = Convert.ToHexString(SHA256.HashData(bytes));
        object Asset(long id, string name, long size, string? digest = null) => new { id, name, size, state = "uploaded", digest };
        object Release(string version, bool draft = false, bool? prerelease = null) => new
        {
            tag_name = "v" + version, draft, prerelease = prerelease ?? version.Contains("beta"),
            assets = new[] { Asset(1, "update.json", 250), Asset(2, $"RTSPView-Setup-{version}-win-x64.exe", bytes.Length, "sha256:" + checksum) }
        };
        var manifestVersion = "1.0.32";
        var badHash = false; var unsafeRedirect = false; var truncate = false; var calls = 0;
        var handler = new Handler(request =>
        {
            calls++;
            if (request.Headers.Authorization is not null) throw new Exception("Updater must not send credentials.");
            if (request.RequestUri!.Host == "release-assets.githubusercontent.com")
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(truncate ? bytes[..^1] : bytes) };
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/latest")) return Json(Release("1.0.32"));
            if (path.EndsWith("/releases")) return Json(new[] { Release("1.0.40", prerelease: false), Release("1.0.34-beta.1", draft: true), Release("1.0.32-beta.2"), Release("1.0.32-beta.10"), Release("1.0.32-beta.3") });
            if (path.EndsWith("/assets/1")) return Json(new UpdateManifest(manifestVersion, $"RTSPView-Setup-{manifestVersion}-win-x64.exe", badHash ? new string('0', 64) : checksum, DateTimeOffset.UtcNow));
            if (path.EndsWith("/assets/2"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri(unsafeRedirect ? "http://untrusted.example/file" : "https://release-assets.githubusercontent.com/test/installer");
                return response;
            }
            throw new Exception("Unexpected release request.");
        });
        using var source = new GitHubUpdateSource(new HttpClient(handler), "example/RTSPView");
        var stable = await source.FindAsync("stable") ?? throw new Exception("Stable missing.");
        Check(stable.Manifest.Version == "1.0.32", "stable selection");
        var before = calls; await source.FindAsync("stable"); Check(calls == before, "metadata cache");
        var destination = Path.Combine(root, "synthetic.exe");
        await source.DownloadAsync(stable, destination);
        Check(File.ReadAllBytes(destination).SequenceEqual(bytes), "verified installer download");
        manifestVersion = "1.0.32-beta.10";
        Check((await source.FindAsync("beta"))?.Manifest.Version == manifestVersion, "highest beta revision, excluding stable/drafts");
        manifestVersion = "1.0.31";
        await Reject(() => source.FindAsync("stable", refresh: true), "manifest/tag mismatch");
        manifestVersion = "1.0.32"; badHash = true;
        await Reject(() => source.FindAsync("stable", refresh: true), "asset digest mismatch");
        badHash = false; unsafeRedirect = true;
        await Reject(() => source.DownloadAsync(stable, destination), "untrusted redirect");
        unsafeRedirect = false; truncate = true;
        await Reject(() => source.DownloadAsync(stable, destination), "truncated download");
        Check(File.ReadAllBytes(destination).SequenceEqual(bytes), "failed download preserves prior verified installer");
        Check(!Directory.EnumerateFiles(root, "*.partial").Any(), "partial download removed");
        Console.WriteLine("GitHub update checks passed: channels, beta ordering, caching, manifests, digest, redirects, no credentials and failed downloads.");
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("Failed GitHub update check: " + name); }
    private static async Task Reject(Func<Task> action, string name)
    {
        try { await action(); } catch (InvalidDataException) { return; }
        throw new Exception("Expected rejection: " + name);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
