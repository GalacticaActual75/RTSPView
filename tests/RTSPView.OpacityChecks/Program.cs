using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

internal static class Program
{
    private static readonly List<MediaPlayer> Players = [];
    private static readonly List<Media> MediaFiles = [];
    private static readonly List<Window> Overlays = [];
    private static readonly List<FrameworkElement> Views = [];
    private static RTSPView.Viewer.CameraTile? CompositedTile;
    [STAThread]
    private static void Main(string[] args)
    {
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "progress.log"), "Starting test" + Environment.NewLine);
        Core.Initialize();
        using var vlc = new LibVLC("--no-video-title-show", "--no-osd");
        var folder = Path.Combine(Path.GetTempPath(), "RTSPView-OpacityChecks");
        Directory.CreateDirectory(folder);
        var blue = MakeVideo(folder, "blue", 41, 240, 110);
        var red = MakeVideo(folder, "red", 81, 90, 240);
        var app = new Application();
        var main = new Window { Title = "RTSPView opacity renderer comparison", Width = 1040, Height = 640, Left = 40, Top = 40, Background = Brushes.Black };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(new TextBlock { Text = "50% red video over blue video should be purple. Left: Direct3D11 | Center: GDI | Right: WPF frames\nPress 2 / 5 / 0 for 20% / 50% / 100%. Close this window to end the test.", Foreground = Brushes.White, Margin = new Thickness(12), FontSize = 16 });
        var host = new VideoView();
        Grid.SetRow(host, 1); grid.Children.Add(host); main.Content = grid;
        Attach(host, vlc, blue, "direct3d11");
        main.Loaded += async (_, _) =>
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "progress.log"), "Main loaded" + Environment.NewLine);
            await Task.Delay(500);
            Players[0].Play(MediaFiles[0]);
            var origin = host.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(host);
            foreach (var mode in new[] { "direct3d11", "wingdi", "composited" })
            {
                FrameworkElement view;
                if (mode == "composited")
                {
                    // Exercise the real tile's visual tree and player lifecycle.
                    // Reflection only substitutes a local file for its RTSP input;
                    // the production application keeps its RTSP-only validation.
                    var tile = new RTSPView.Viewer.CameraTile();
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    typeof(RTSPView.Viewer.CameraTile).GetField("_snapshotDirectory", flags)!.SetValue(tile, Path.Combine(folder, "snapshots"));
                    tile.Initialize(vlc, new RTSPView.Infrastructure.RollingFileLogger(Path.Combine(folder, "logs")),
                        new RTSPView.Core.CameraSettings { Slot = 10, Enabled = false }, true, compositedVideo: true);
                    if (tile.GetNativeVideoHandle() != IntPtr.Zero) throw new InvalidOperationException("Composited tile created a native video host");
                    var player = (MediaPlayer)typeof(RTSPView.Viewer.CameraTile).GetField("_player", flags)!.GetValue(tile)!;
                    var media = new Media(vlc, new Uri(red));
                    media.AddOption(":avcodec-hw=none"); media.AddOption(":input-repeat=65535"); media.AddOption(":no-audio");
                    typeof(RTSPView.Viewer.CameraTile).GetField("_media", flags)!.SetValue(tile, media);
                    tile.ApplyVideoSizing(100, 50, 50, 270, 260);
                    tile.ApplyViewportEdgeSmoothing(new RTSPView.Core.DoorbellOverlaySettings(), 270, 260);
                    Players.Add(player); MediaFiles.Add(media); view = tile; CompositedTile = tile;
                }
                else { var nativeView = new VideoView(); Attach(nativeView, vlc, red, mode); view = nativeView; }
                var overlay = new Window { Owner = main, Title = mode, AllowsTransparency = mode == "composited", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Background = Brushes.Black, Width = 270, Height = 260, Left = origin.X / dpi.DpiScaleX + 35 + Overlays.Count * 325, Top = origin.Y / dpi.DpiScaleY + 85, Content = view };
                Views.Add(view);
                Overlays.Add(overlay); overlay.Show();
                SetAlpha(overlay, 128);
                await Task.Delay(300);
                Players[^1].Play(MediaFiles[^1]);
            }
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "progress.log"), "All players started" + Environment.NewLine);
            if (args.Contains("--auto"))
            {
                try { await VerifyAsync(main, vlc, blue); }
                catch (Exception error) { Environment.ExitCode = 1; File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "results.json"), System.Text.Json.JsonSerializer.Serialize(new { error = error.ToString() })); }
                main.Close();
            }
        };
        main.KeyDown += (_, e) =>
        {
            var alpha = e.Key switch { System.Windows.Input.Key.D2 => (byte)51, System.Windows.Input.Key.D5 => (byte)128, _ => (byte)255 };
            foreach (var overlay in Overlays) SetAlpha(overlay, alpha);
        };
        main.Closed += (_, _) => { foreach (var player in Players.Take(Players.Count - (CompositedTile is null ? 0 : 1))) { player.Stop(); player.Dispose(); } CompositedTile?.Dispose(); foreach (var media in MediaFiles) media.Dispose(); };
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "progress.log"), "Entering UI loop" + Environment.NewLine);
        app.Run(main);
    }
    private static void Attach(VideoView view, LibVLC vlc, string path, string mode)
    {
        var player = new MediaPlayer(vlc) { EnableHardwareDecoding = true };
        var media = new Media(vlc, new Uri(path));
        media.AddOption(":vout=" + mode); media.AddOption(":input-repeat=65535"); media.AddOption(":no-audio");
        view.MediaPlayer = player; Players.Add(player); MediaFiles.Add(media);
    }
    private static string MakeVideo(string folder, string name, byte y, byte u, byte v)
    {
        var path = Path.Combine(folder, name + ".y4m");
        using var stream = File.Create(path);
        stream.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W160 H120 F15:1 Ip A1:1 C420jpeg\n"));
        for (var frame = 0; frame < 150; frame++)
        {
            stream.Write(Encoding.ASCII.GetBytes("FRAME\n"));
            var luma = Enumerable.Repeat(y, 160 * 120).ToArray();
            for (var row = 0; row < 24; row++) for (var column = frame % 140; column < frame % 140 + 12; column++) luma[row * 160 + column] = 180;
            stream.Write(luma); stream.Write(Enumerable.Repeat(u, 160 * 120 / 4).ToArray()); stream.Write(Enumerable.Repeat(v, 160 * 120 / 4).ToArray());
        }
        return path;
    }
    private static void SetAlpha(Window window, byte alpha)
    {
        RTSPView.Viewer.OverlayWindowOpacity.Apply(window, (int)Math.Round(alpha * 100d / 255));
    }
    private static async Task VerifyAsync(Window main, LibVLC vlc, string blue)
    {
        main.Activate();
        await Task.Delay(2500);
        var points = Views.Select(v => v.PointToScreen(new Point(v.ActualWidth / 2, v.ActualHeight / 2 + 25))).ToArray();
        foreach (var overlay in Overlays) overlay.Hide();
        await Task.Delay(500);
        var background = points.Select(ReadPixel).ToArray();
        foreach (var overlay in Overlays) { SetAlpha(overlay, 255); overlay.Show(); }
        await Task.Delay(700);
        var opaque = points.Select(ReadPixel).ToArray();
        var results = new List<object>();
        var allPassed = true;
        foreach (var percent in new[] { 50, 20, 100 })
        {
            foreach (var overlay in Overlays) RTSPView.Viewer.OverlayWindowOpacity.Apply(overlay, percent);
            await Task.Delay(700);
            for (var index = 0; index < Overlays.Count; index++)
            {
                var actual = ReadPixel(points[index]);
                var attributesAvailable = GetLayeredWindowAttributes(new WindowInteropHelper(Overlays[index]).Handle, out _, out var nativeAlpha, out _);
                var expected = opaque[index].Zip(background[index], (a, b) => (a * percent + b * (100 - percent)) / 100).ToArray();
                // A baseline must show both a red foreground and a blue background;
                // an obscured/unsupported capture cannot accidentally count as a pass.
                var baselineValid = opaque[index][0] > 180 && opaque[index][2] < 50 && background[index][2] > 180 && background[index][0] < 50;
                var passed = baselineValid && actual.Zip(expected, (a, b) => Math.Abs(a - b)).All(d => d < 18);
                if (Overlays[index].Title == "composited") allPassed &= passed;
                results.Add(new { renderer = Overlays[index].Title, percent, background = background[index], opaque = opaque[index], actual, expected,
                    passed,
                    captureAvailable = actual.All(c => c >= 0), attributesAvailable, nativeAlpha,
                    decodedFrames = Players[index + 1].Media?.Statistics.DecodedVideo,
                    displayedFrames = Players[index + 1].Media?.Statistics.DisplayedPictures });
            }
        }
        var composited = Overlays[^1];
        var compHandle = new WindowInteropHelper(composited).Handle;
        var scale = VisualTreeHelper.GetDpi(composited);
        var region = CreateEllipticRgn(0, 0, (int)(composited.ActualWidth * scale.DpiScaleX), (int)(composited.ActualHeight * scale.DpiScaleY));
        if (SetWindowRgn(compHandle, region, true) == 0) { DeleteObject(region); throw new InvalidOperationException("Test clipping region failed"); }
        RTSPView.Viewer.OverlayWindowOpacity.Apply(composited, 50);
        await Task.Delay(500);
        var corner = composited.PointToScreen(new Point(8, composited.ActualHeight - 8));
        var outside = ReadPixel(corner);
        var clippingPassed = outside[2] > 180 && outside[0] < 50;
        results.Add(new { test = "ellipse exterior shows host video", actual = outside, passed = clippingPassed });
        allPassed &= clippingPassed;
        SetWindowRgn(compHandle, IntPtr.Zero, true);
        composited.Hide(); await Task.Delay(300);
        var hidden = ReadPixel(points[^1]);
        var hiddenPassed = hidden[2] > 180 && hidden[0] < 50;
        results.Add(new { test = "hidden overlay reveals host", actual = hidden, passed = hiddenPassed }); allPassed &= hiddenPassed;
        composited.Show(); await Task.Delay(300);
        var restored = ReadPixel(points[^1]);
        var restoredPassed = restored[0] is > 90 and < 160 && restored[2] is > 90 and < 160;
        results.Add(new { test = "restored overlay retains opacity", actual = restored, passed = restoredPassed }); allPassed &= restoredPassed;
        using var alternate = new Media(vlc, new Uri(blue));
        alternate.AddOption(":avcodec-hw=none");
        Players[^1].Stop();
        Players[^1].Play(alternate);
        RTSPView.Viewer.OverlayWindowOpacity.Apply(composited, 100);
        await Task.Delay(1500);
        var restarted = ReadPixel(points[^1]);
        var restartPassed = restarted[2] > 180 && restarted[0] < 50;
        results.Add(new { test = "stop/restart replaces old frame with new video", actual = restarted, passed = restartPassed }); allPassed &= restartPassed;
        Players[^1].Stop();
        Players[^1].Play(MediaFiles[^1]);
        await Task.Delay(1200);
        var snapshot = Path.Combine(AppContext.BaseDirectory, "composited-snapshot.png");
        var snapshotPassed = Players[^1].TakeSnapshot(0, snapshot, 160, 0);
        await Task.Delay(500);
        snapshotPassed &= File.Exists(snapshot) && new FileInfo(snapshot).Length > 0;
        results.Add(new { test = "snapshot from composited player", passed = snapshotPassed }); allPassed &= snapshotPassed;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "results.json"), System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (!allPassed) Environment.ExitCode = 1;
    }
    private static int[] ReadPixel(Point point)
    {
        // Sample only a known pixel in this test's video viewport; no desktop
        // screenshot or unrelated window content is saved.
        var dc = GetDC(IntPtr.Zero);
        try { var rgb = GetPixel(dc, (int)point.X, (int)point.Y); return rgb == uint.MaxValue ? [-1, -1, -1] : [(int)(rgb & 255), (int)((rgb >> 8) & 255), (int)((rgb >> 16) & 255)]; }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint key, out byte alpha, out uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
}
