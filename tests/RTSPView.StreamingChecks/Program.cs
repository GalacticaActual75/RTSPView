using LibVLCSharp.Shared;
using RTSPView.Core;
using RTSPView.Infrastructure;
using System.IO;

var origin = Environment.GetEnvironmentVariable("RTSPVIEW_STREAM_FIXTURE") ?? throw new Exception("Run tests/stream-playback.checks.py to start the local media fixture.");
Console.WriteLine("Initializing LibVLC");
Core.Initialize(Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64"));
using var engine = new LibVLC("--vout=dummy", "--aout=dummy", "--avcodec-hw=none", "--no-video-title-show");

Console.WriteLine("LibVLC initialized");
foreach (var (path, mode) in new[] { ("/sample.mp4", StreamSourceMode.Direct), ("/stream.m3u8", StreamSourceMode.Auto), ("/watch", StreamSourceMode.YtDlp), ("/watch-hls", StreamSourceMode.Auto) })
{
    Console.WriteLine($"Starting {path}");
    var settings = new CameraSettings { RtspUrl = origin + path, SourceMode = mode };
    using var resolution = new CancellationTokenSource(TimeSpan.FromSeconds(70));
    using var lease = await StreamResolver.ResolveAsync(settings, resolution.Token);
    Console.WriteLine($"Resolved {path}");
    using var media = new Media(engine, lease.Uri);
    media.AddOption(":avcodec-hw=none");
    foreach (var option in settings.ToMediaOptions()) media.AddOption(option);
    using var player = new MediaPlayer(engine) { EnableHardwareDecoding = false };
    if (!player.Play(media)) throw new Exception($"Playback rejected: {path}");
    Console.WriteLine($"Playing {path}");
    var deadline = DateTime.UtcNow.AddSeconds(20);
    while (media.Statistics.DecodedVideo < 3 && DateTime.UtcNow < deadline) await Task.Delay(100);
    if (media.Statistics.DecodedVideo < 3) throw new Exception($"No decoded frames: {path}, state={player.State}");
    player.Stop();
    Console.WriteLine($"PASS decoded video: {path} via {lease.Provider}");
}
using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)))
{
    try
    {
        using var unexpected = await StreamResolver.ResolveAsync(new CameraSettings { RtspUrl = origin + "/hang", SourceMode = StreamSourceMode.YtDlp }, cancellation.Token);
        throw new Exception("Cancelled resolution completed unexpectedly.");
    }
    catch (OperationCanceledException) { Console.WriteLine("PASS in-flight resolver cancellation"); }
}
await TileChecks.Run(origin);
