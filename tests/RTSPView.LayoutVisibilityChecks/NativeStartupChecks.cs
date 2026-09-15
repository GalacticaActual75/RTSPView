using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using RTSPView.Core;
using RTSPView.Infrastructure;
using RTSPView.Viewer;

internal static class NativeStartupChecks
{
    public static async Task Run()
    {
        Core.Initialize();
        using var vlc = new LibVLC("--no-video-title-show", "--no-osd");
        var tile = new CameraTile { Visibility = Visibility.Collapsed };
        var window = new Window { Content = tile, Width = 400, Height = 300, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false };
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // An unavailable local source is enough to observe the attachment before connection.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        MediaPlayer Player() => (MediaPlayer)typeof(CameraTile).GetField("_player", flags)!.GetValue(tile)!;
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            tile.Initialize(vlc, new RollingFileLogger(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RTSPView-startup-checks")),
                new CameraSettings { Enabled = true, RtspUrl = $"rtsp://127.0.0.1:{port}/test" }, true);
            await Task.Delay(150);
            if (Player().Media is not null) throw new Exception("Native playback started before the collapsed host was ready");
            Console.WriteLine("PASS collapsed startup waits for a native destination");
            tile.SetWallVisibility(false);
            window.UpdateLayout();
            await CheckAttached("unassigned camera starts on a hidden native destination");
            if (tile.IsVisible) throw new Exception("Background camera became visible");
            var backgroundPlayer = Player();
            tile.Start();
            await CheckAttached("unassigned camera restarts in the background");
            typeof(CameraTile).GetMethod("StartPlayer", flags)!.Invoke(tile, [true]);
            await CheckAttached("unassigned replacement player attaches in the background");
            backgroundPlayer = Player();
            tile.SetWallVisibility(true);
            window.UpdateLayout();
            await CheckAttached("revealing camera retains native destination");
            if (!ReferenceEquals(backgroundPlayer, Player())) throw new Exception("Reveal replaced the warm player");
            tile.Start();
            await CheckAttached("manual restart retains the native destination");
            typeof(CameraTile).GetMethod("StartPlayer", flags)!.Invoke(tile, [true]);
            await CheckAttached("replacement player attaches before playback");
        }
        finally { tile.Dispose(); window.Close(); }

        async Task CheckAttached(string label)
        {
            for (var i = 0; i < 240; i++)
            {
                await Task.Delay(50);
                if (Player().Hwnd != IntPtr.Zero && Player().Hwnd == tile.GetNativeVideoHandle() && Player().Media is not null)
                { Console.WriteLine("PASS " + label); return; }
            }
            throw new Exception(label + ": player has no matching native destination");
        }
    }
}
