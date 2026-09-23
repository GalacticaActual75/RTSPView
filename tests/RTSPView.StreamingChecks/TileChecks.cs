using System.IO;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using RTSPView.Core;
using RTSPView.Infrastructure;
using RTSPView.Viewer;

internal static class TileChecks
{
    public static Task Run(string origin)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "RTSPView-stream-tile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var prior = Environment.GetEnvironmentVariable("RTSPVIEW_DATA_DIR");
            Environment.SetEnvironmentVariable("RTSPVIEW_DATA_DIR", directory);
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/{typeof(CameraTile).Assembly.GetName().Name};component/ProductTheme.xaml", UriKind.Relative) });
                using var engine = new LibVLC("--no-video-title-show", "--no-audio");
                using var tile = new CameraTile();
                var window = new Window { Content = tile, Width = 320, Height = 180, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
                var settings = new CameraSettings { RtspUrl = origin + "/watch", SourceMode = StreamSourceMode.YtDlp };
                tile.Initialize(engine, new RollingFileLogger(directory), settings, false, compositedVideo: true);
                // Replace an in-flight request immediately; a late result must not win.
                tile.Apply(settings with { RtspUrl = origin + "/watch-hls" });
                window.Show();
                using var deniedTile = new CameraTile();
                deniedTile.Initialize(engine, new RollingFileLogger(directory), settings with { Slot = 2, RtspUrl = origin + "/watch-denied" }, false, compositedVideo: false);
                var deniedWindow = new Window { Content = deniedTile, Width = 320, Height = 180, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
                deniedWindow.Show();
                var sawResolving = false;
                var sawRefusal = false;
                var stage = 0;
                var startedAt = DateTimeOffset.UtcNow;
                var deadline = DateTime.UtcNow.AddSeconds(70);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (_, _) =>
                {
                    try
                    {
                        tile.Tick();
                        deniedTile.Tick();
                        var denied = deniedTile.GetTelemetry();
                        if (denied.State == "Resolving")
                        {
                            sawResolving = true;
                            if (denied.FrameWarning is not null) throw new Exception("Website resolution incorrectly reports stale video");
                        }
                        if (denied.LastError?.Contains("HTTP 403") == true && denied.ReconnectCount > 0) sawRefusal = true;
                        if (DateTime.UtcNow > deadline) throw new Exception($"Tile did not recover: stage={stage}, {tile.Status.LastError}; refusal={denied.LastError}");
                        if (stage >= 2 && sawResolving && sawRefusal)
                        {
                            timer.Stop(); deniedTile.Dispose(); deniedWindow.Close(); tile.Dispose(); window.Close(); app.Shutdown(); done.TrySetResult(); return;
                        }
                        if (stage >= 2) return;
                        if (tile.Status.State != CameraConnectionState.Live || tile.Status.LastFrameAt is null || tile.Status.LastFrameAt < startedAt) return;
                        if (stage++ == 0) { startedAt = DateTimeOffset.UtcNow; tile.Start(); }
                    }
                    catch (Exception error)
                    {
                        timer.Stop(); deniedTile.Dispose(); deniedWindow.Close(); tile.Dispose(); window.Close(); app.Shutdown(); done.TrySetException(error);
                    }
                };
                timer.Start();
                app.Run();
            }
            catch (Exception error) { done.TrySetException(error); }
            finally
            {
                Environment.SetEnvironmentVariable("RTSPVIEW_DATA_DIR", prior);
                // Logs stay in the isolated temporary directory for failure diagnosis.
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task.ContinueWith(task => { task.GetAwaiter().GetResult(); Console.WriteLine("PASS actual tile: in-flight source replacement, decoded frames, restart, native website resolving/refusal telemetry and disposal"); });
    }
}
