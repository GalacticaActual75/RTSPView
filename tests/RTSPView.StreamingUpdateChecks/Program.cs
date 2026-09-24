using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using RTSPView.Infrastructure;

var root = Path.Combine(Path.GetTempPath(), "RTSPView-streaming-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
if (args.Contains("--live"))
{
    try
    {
        var live = new StreamingUpdates(root);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RTSPView-StreamingChecks/1.0");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await live.CheckAsync(client, timeout.Token);
        if (live.Status().Active is null) throw new Exception("Published package did not activate: " + live.Status().Message);
        var selected = await live.SelectAsync(timeout.Token);
        using (selected.Lease)
        {
            if (selected.Path == StreamingUpdates.BundledHelper) throw new Exception("Published helper failed startup");
            Console.WriteLine("PASS public signed feed, package download, verification, extraction, health check and active selection");
            Console.WriteLine(JsonSerializer.Serialize(live.DisplayStatus()));
        }
    }
    finally { Directory.Delete(root, true); }
    return;
}
using var key = RSA.Create(2048);
var probes = 0;
var broken = new HashSet<string>();
var updater = new StreamingUpdates(root, key.ExportSubjectPublicKeyInfoPem(), (path, token) =>
{
    probes++;
    if (!File.Exists(path) || broken.Contains(Path.GetFileName(Path.GetDirectoryName(path))!)) throw new InvalidDataException("fixture");
    return Task.CompletedTask;
});
var feed = new Feed();
using var http = new HttpClient(feed);
var statePath = Path.Combine(root, "Streaming", "state.json");
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
void Due() => DurableJson.Write(statePath, updater.Status() with { LastCheck = null }, false);
byte[] Zip(string entry, string content)
{
    using var bytes = new MemoryStream();
    using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
    { using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write(content); }
    return bytes.ToArray();
}
byte[] Sign(StreamingManifest m)
{
    var payload = JsonSerializer.SerializeToUtf8Bytes(m);
    return JsonSerializer.SerializeToUtf8Bytes(new { payload = Convert.ToBase64String(payload),
        signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) });
}
StreamingManifest Release(long sequence, byte[]? zip = null)
{
    feed.Package = zip ?? Zip("stream-resolver.exe", sequence.ToString());
    var m = new StreamingManifest(sequence, 1, "win-x64", $"https://github.com/GalacticaActual75/RTSPView/releases/download/streaming-{sequence}/package.zip",
        Convert.ToHexString(SHA256.HashData(feed.Package)), feed.Package.Length, "test-yt", "test-streamlink");
    feed.Manifest = Sign(m);
    return m;
}
try
{
    var first = Release(1);
    await updater.CheckAsync(http, default);
    Check(updater.Status().Active == first.Sha256 && probes == 1, "signed package is health checked and activated");
    var calls = feed.Calls;
    await updater.CheckAsync(http, default);
    Check(feed.Calls == calls, "daily checks are persisted and rate limited");
    var lease = await updater.SelectAsync(default);
    Check(lease.Path.Contains(first.Sha256), "resolver selects installed package");
    Due(); var second = Release(2); await updater.CheckAsync(http, default);
    Check(updater.Status().Previous == first.Sha256, "previous working package retained");
    Due(); var third = Release(3); await updater.CheckAsync(http, default);
    Check(File.Exists(lease.Path), "active relay lease protects an older package from cleanup");
    lease.Lease?.Dispose();
    Due(); var fourth = Release(4); await updater.CheckAsync(http, default);
    Check(!File.Exists(lease.Path), "unused old package removed after lease closes");
    broken.Add(fourth.Sha256);
    var fallback = await updater.SelectAsync(default); fallback.Lease?.Dispose();
    Check(fallback.Path.Contains(third.Sha256) && updater.Status().Rejected == fourth.Sha256, "broken active helper rolls back to previous helper");
    Due(); Release(2); await updater.CheckAsync(http, default);
    Check(updater.Status().Active == third.Sha256, "older signed releases cannot undo rollback protection");
    Due(); var fifth = Release(5); feed.Package[feed.Package.Length / 2] ^= 1; await updater.CheckAsync(http, default);
    Check(updater.Status().Active == third.Sha256, "corrupt download leaves current helper unchanged");
    Due(); Release(6, Zip("../escape.exe", "bad")); await updater.CheckAsync(http, default);
    Check(updater.Status().Active == third.Sha256 && !File.Exists(Path.Combine(root, "escape.exe")), "archive traversal rejected before activation");
    var incompatible = fifth with { Protocol = 99 };
    try { updater.Verify(Sign(incompatible)); throw new Exception("accepted protocol"); } catch (InvalidDataException) { }
    Check(true, "incompatible protocol rejected");
    using var wrong = RSA.Create(2048);
    var untrusted = new StreamingUpdates(root, wrong.ExportSubjectPublicKeyInfoPem());
    try { untrusted.Verify(Sign(fifth)); throw new Exception("accepted signature"); } catch (InvalidDataException) { }
    Check(true, "untrusted signature rejected");
    var external = fifth with { Url = "https://example.com/package.zip" };
    try { updater.Verify(Sign(external)); throw new Exception("accepted host"); } catch (InvalidDataException) { }
    Check(true, "non-release package locations rejected");
    Due(); feed.Fail = true; await updater.CheckAsync(http, default);
    Check(updater.Status().Active == third.Sha256 && updater.Status().LastCheck is not null, "network failures retain working package and back off");
    feed.Fail = false;
    broken.Add(third.Sha256);
    var bundled = await updater.SelectAsync(default);
    Check(bundled.Path == StreamingUpdates.BundledHelper, "failed previous helper falls back to bundled helper");
    Due(); Release(7);
    await Task.WhenAll(updater.CheckAsync(http, default), updater.CheckAsync(http, default));
    Check(updater.Status().HighestSequence == 7, "concurrent update checks serialize safely");
    // A slow download must not block new stream selection.
    Due(); Release(8); feed.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var update = updater.CheckAsync(http, default);
    await feed.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var during = await updater.SelectAsync(deadline.Token); during.Lease?.Dispose();
    feed.Hold.SetResult(); await update;
    Check(true, "new streams can start while an update downloads");
    Due(); var interrupted = Release(9); feed.Hold = null;
    Directory.CreateDirectory(Path.Combine(root, "Streaming", interrupted.Sha256));
    await updater.CheckAsync(http, default);
    Check(updater.Status().Active == interrupted.Sha256, "interrupted activation recovers on the next update");
    Console.WriteLine("Streaming update checks passed.");
}
finally { Directory.Delete(root, true); }

sealed class Feed : HttpMessageHandler
{
    public byte[] Manifest = [], Package = [];
    public int Calls;
    public bool Fail;
    public TaskCompletionSource? Hold;
    public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++;
        if (Fail) return new(HttpStatusCode.ServiceUnavailable);
        if (request.RequestUri!.AbsolutePath.EndsWith(".zip") && Hold is not null)
        { Started.TrySetResult(); await Hold.Task.WaitAsync(token); }
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith("manifest.json") ? Manifest : Package) };
    }
}
