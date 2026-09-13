using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using RTSPView.Core;
using RTSPView.Infrastructure;
using RTSPView.Viewer;

internal static class StreamTelemetryChecks
{
    public static async Task Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RTSPView-stream-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "synthetic.y4m");
        using (var output = File.Create(file))
        {
            output.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W160 H120 F30:1 Ip A1:1 C420jpeg\n"));
            for (var frame = 0; frame < 180; frame++)
            {
                output.Write(Encoding.ASCII.GetBytes("FRAME\n"));
                output.Write(Enumerable.Repeat((byte)(40 + frame % 100), 160 * 120).ToArray());
                output.Write(Enumerable.Repeat((byte)128, 160 * 120 / 2).ToArray());
            }
        }
        using var engine = new LibVLC("--no-video-title-show", "--no-osd", "--no-snapshot-preview");
        var tile = new CameraTile();
        var window = new Window { Content = tile, Width = 400, Height = 300, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            tile.Initialize(engine, new RollingFileLogger(Path.Combine(directory, "logs")), new CameraSettings { Enabled = true }, false, compositedVideo: true);
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            // Substitute a deterministic local fixture only in the test; production remains RTSP-only.
            var player = (MediaPlayer)typeof(CameraTile).GetField("_player", flags)!.GetValue(tile)!;
            var media = new Media(engine, new Uri(file));
            media.AddOption(":avcodec-hw=none"); media.AddOption(":no-audio");
            typeof(CameraTile).GetField("_media", flags)!.SetValue(tile, media);
            if (!player.Play(media)) throw new Exception("Synthetic video did not start");
            for (var attempt = 0; attempt < 60; attempt++)
            {
                await Task.Delay(100); tile.Tick();
                var telemetry = tile.GetTelemetry();
                if (telemetry.Width == 160 && telemetry.Height == 120 && telemetry.Fps > 0 &&
                    !string.IsNullOrWhiteSpace(telemetry.Codec) && telemetry.BitrateKbps > 0 && telemetry.LastFrameAt is not null)
                {
                    if (telemetry.Decoder != "Software decoding") throw new Exception("Software stream mislabeled as hardware");
                    Console.WriteLine($"PASS decoded fixture telemetry: {telemetry.Width}x{telemetry.Height}, {telemetry.Codec}, measured FPS/bitrate, software decoder and real frame timestamp");
                    if (!await tile.RefreshSnapshotAsync() || tile.GetTelemetry().SnapshotCapturedAt is null)
                        throw new Exception("Snapshot capture did not publish its completion timestamp");
                    Console.WriteLine("PASS decoded fixture snapshot capture and completion telemetry");
                    var statusField = typeof(CameraTile).GetField("_status", flags)!;
                    var status = (CameraRuntimeStatus)statusField.GetValue(tile)!;
                    statusField.SetValue(tile, status with { State = CameraConnectionState.Live, LastError = "Previous watchdog failure", ReconnectCount = 2 });
                    for (var recovery = 0; recovery < 20; recovery++)
                    {
                        await Task.Delay(100); tile.Tick();
                        if (tile.GetTelemetry().LastError is null) break;
                    }
                    if (tile.GetTelemetry().LastError is not null || tile.GetTelemetry().ReconnectCount != 2)
                        throw new Exception("Real frame recovery did not clear the active error while preserving reconnect history");
                    Console.WriteLine("PASS recovered stream clears active error without restarting or losing reconnect history");
                    // Keep the deterministic media but give the tile the same configuration
                    // shape as a configured automation-only overlay. Applying its display
                    // mode must not replace this already-decoding player or media.
                    var settingsField = typeof(CameraTile).GetField("_settings", flags)!;
                    var warmSettings = new CameraSettings { Enabled = true, RtspUrl = "rtsp://example.test/warm-overlay" };
                    settingsField.SetValue(tile, warmSettings);
                    var image = (System.Windows.Controls.Image)tile.FindName("CompositedImage");
                    var frameSource = image.Source;
                    for (var cycle = 0; cycle < 2; cycle++)
                    {
                        var before = tile.GetTelemetry().LastFrameAt;
                        window.Hide(); tile.SetWallVisibility(false);
                        tile.Apply(warmSettings with { Enabled = false });
                        await Task.Delay(400); tile.Tick();
                        if (tile.IsVisible || window.IsVisible || ((UIElement)tile.FindName("OverlayRoot")).IsVisible)
                            throw new Exception("Hidden warm overlay exposed a window or connection stats");
                        if (tile.GetTelemetry().LastFrameAt <= before || !player.IsPlaying)
                            throw new Exception("Hidden overlay stopped decoding frames");
                        tile.Apply(warmSettings); tile.SetWallVisibility(true); window.Show();
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        if (!ReferenceEquals(player, typeof(CameraTile).GetField("_player", flags)!.GetValue(tile)) ||
                            !ReferenceEquals(media, typeof(CameraTile).GetField("_media", flags)!.GetValue(tile)) ||
                            !ReferenceEquals(frameSource, image.Source) || tile.GetTelemetry().State != CameraConnectionState.Live.ToString())
                            throw new Exception("Revealing a warm overlay restarted playback or discarded its decoded frame");
                    }
                    tile.Apply(warmSettings with { Enabled = false, RtspUrl = "" });
                    for (var wait = 0; wait < 20 && player.IsPlaying; wait++) await Task.Delay(100);
                    if (player.IsPlaying) throw new Exception("Clearing overlay URL left background playback running");
                    Console.WriteLine("PASS hidden overlay keeps decoding, reveal retains player/media/frame, mode changes do not restart, and clearing URL stops playback");
                    return;
                }
            }
            throw new Exception("Decoded fixture telemetry did not report dimensions, codec, progress and bitrate");
        }
        finally { tile.Dispose(); window.Close(); }
    }
}
