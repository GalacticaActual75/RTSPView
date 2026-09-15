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
    public static async Task Run(bool preserveWholeFrame = false, bool nativeBackground = false)
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
            tile.Initialize(engine, new RollingFileLogger(Path.Combine(directory, "logs")), new CameraSettings { Enabled = true }, false, compositedVideo: !nativeBackground, preserveWholeFrame: preserveWholeFrame);
            if (nativeBackground) tile.SetWallVisibility(false);
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            // Substitute a deterministic local fixture only in the test; production remains RTSP-only.
            var player = (MediaPlayer)typeof(CameraTile).GetField("_player", flags)!.GetValue(tile)!;
            var playbackEngine = (LibVLC)typeof(CameraTile).GetField("_libVlc", flags)!.GetValue(tile)!;
            var media = new Media(playbackEngine, new Uri(file));
            if (nativeBackground)
            {
                player.Hwnd = tile.GetNativeVideoHandle();
                // Hosted CI has no accelerated desktop; exercise the real HWND
                // with VLC's software Windows output rather than Direct3D.
                media.AddOption(":vout=wingdi");
            }
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
                    if (nativeBackground)
                    {
                        var before = tile.GetTelemetry().LastFrameAt;
                        await Task.Delay(500); tile.Tick();
                        if (tile.IsVisible || ((UIElement)tile.FindName("OverlayRoot")).IsVisible ||
                            !player.IsPlaying || tile.GetTelemetry().LastFrameAt <= before)
                            throw new Exception("Unassigned native camera failed to deliver hidden frames");
                        var handle = player.Hwnd;
                        tile.SetWallVisibility(true); window.UpdateLayout();
                        await Task.Delay(200);
                        if (player.Hwnd != handle || !ReferenceEquals(media, player.Media) && player.Media?.Mrl != media.Mrl || !player.IsPlaying)
                            throw new Exception("Assigning native camera interrupted playback");
                        tile.SetWallVisibility(false);
                        tile.Apply(new CameraSettings { Enabled = false });
                        for (var wait = 0; wait < 30 && player.IsPlaying; wait++) await Task.Delay(100);
                        if (player.IsPlaying) throw new Exception("Disabled background camera kept playing");
                        Console.WriteLine("PASS unassigned native camera decodes and snapshots, reveal retains playback, disable stops it");
                        return;
                    }
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
                    tile.ApplyOverlayPreferences(false, false);
                    var image = (System.Windows.Controls.Image)tile.FindName("CompositedImage");
                    var frameSource = image.Source;
                    if (preserveWholeFrame)
                    {
                        window.Width = 700; window.Height = 300;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        var canvas = (FrameworkElement)tile.FindName("CompositedCanvas");
                        if (!double.IsFinite(image.Width) || !double.IsFinite(image.Height) || image.Width <= 0 || image.Height <= 0 || Math.Abs(image.Width / image.Height - 4d / 3) > .001 || image.Width > canvas.ActualWidth + 1 || image.Height > canvas.ActualHeight + 1)
                            throw new Exception("Original overlay source cropped or distorted its 4:3 frame in a wide tile");
                        Console.WriteLine("PASS original overlay source preserves full frame in wide focus tile");
                    }
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
                        tile.Tick();
                        if (((UIElement)tile.FindName("OverlayPanel")).Visibility != Visibility.Collapsed)
                            throw new Exception("Healthy warm overlay forced connection statistics on reveal");
                        if (!ReferenceEquals(player, typeof(CameraTile).GetField("_player", flags)!.GetValue(tile)) ||
                            !ReferenceEquals(media, typeof(CameraTile).GetField("_media", flags)!.GetValue(tile)) ||
                            !ReferenceEquals(frameSource, image.Source) || tile.GetTelemetry().State != CameraConnectionState.Live.ToString())
                            throw new Exception("Revealing a warm overlay restarted playback or discarded its decoded frame");
                    }
                    tile.ApplyOverlayPreferences(false, true);
                    if (((UIElement)tile.FindName("OverlayPanel")).Visibility != Visibility.Visible)
                        throw new Exception("Overlay ignored the user's statistics preference");
                    tile.ApplyOverlayPreferences(false, false);
                    var liveStatus = (CameraRuntimeStatus)statusField.GetValue(tile)!;
                    statusField.SetValue(tile, liveStatus with { State = CameraConnectionState.Buffering });
                    tile.Tick();
                    if (((UIElement)tile.FindName("OverlayPanel")).Visibility != Visibility.Visible)
                        throw new Exception("Overlay hid a connection problem");
                    statusField.SetValue(tile, liveStatus);
                    tile.Apply(warmSettings with { Enabled = false, RtspUrl = "" });
                    for (var wait = 0; wait < 20 && player.IsPlaying; wait++) await Task.Delay(100);
                    if (player.IsPlaying) throw new Exception("Clearing overlay URL left background playback running");
                    Console.WriteLine("PASS hidden overlay keeps decoding, reveal retains player/media/frame, mode changes do not restart, and clearing URL stops playback");
                    return;
                }
            }
            throw new Exception("Decoded fixture telemetry did not report dimensions, codec, progress and bitrate: " + System.Text.Json.JsonSerializer.Serialize(tile.GetTelemetry()));
        }
        finally { tile.Dispose(); window.Close(); }
    }
}
