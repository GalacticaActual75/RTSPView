using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using RTSPView.Core;
using RTSPView.Infrastructure;
using RTSPView.Viewer;

internal static class SharedFrameChecks
{
    public static async Task Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RTSPView-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "fixture.y4m");
        using (var output = File.Create(file))
        {
            output.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W160 H120 F30:1 Ip A1:1 C420jpeg\n"));
            for (var frame = 0; frame < 600; frame++) { output.Write(Encoding.ASCII.GetBytes("FRAME\n")); output.Write(Enumerable.Repeat((byte)(60 + frame % 100), 160 * 120 * 3 / 2).ToArray()); }
        }
        using var engine = new LibVLC("--no-video-title-show", "--no-osd", "--no-audio", "--no-snapshot-preview");
        var owner = new CameraTile(); var mirror = new CameraTile();
        var grid = new Grid(); grid.Children.Add(owner); grid.Children.Add(mirror);
        var window = new Window { Content = grid, Width = 700, Height = 300, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            var logger = new RollingFileLogger(Path.Combine(directory, "logs"));
            owner.Initialize(engine, logger, new CameraSettings { Slot = 10, Enabled = true }, false, compositedVideo: true);
            var callback = Task.Run(() => typeof(CameraTile).GetMethod("SetOverlay", flags)!.Invoke(owner, new object[] { "Connecting", false }));
            if (!callback.Wait(TimeSpan.FromSeconds(1))) throw new Exception("Decoder event synchronously blocked on the UI dispatcher.");
            mirror.Initialize(engine, logger, new CameraSettings { Slot = 33, Enabled = true }, false, compositedVideo: true, preserveWholeFrame: true, sharedSource: owner);
            if (mirror.OwnsDecoder || typeof(CameraTile).GetField("_player", flags)!.GetValue(mirror) is not null) throw new Exception("Shared view created another decoder.");
            owner.SetWallVisibility(false); window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var player = (MediaPlayer)typeof(CameraTile).GetField("_player", flags)!.GetValue(owner)!;
            var media = new Media(engine, new Uri(file)); media.AddOption(":avcodec-hw=none");
            typeof(CameraTile).GetField("_media", flags)!.SetValue(owner, media);
            if (!player.Play(media)) throw new Exception("Shared fixture did not start.");
            for (var i = 0; i < 50 && owner.GetTelemetry().CompositedUploads == 0; i++) { await Task.Delay(100); owner.Tick(); mirror.Tick(); }
            var image = (System.Windows.Controls.Image)mirror.FindName("CompositedImage");
            if (image.Source is null || owner.GetTelemetry().CompositedUploads == 0 || mirror.GetTelemetry().SharedDecoderSlot != 10) throw new Exception("Visible mirror did not receive hidden owner's frames.");
            if (Math.Abs(image.Width / image.Height - 4d / 3) > .001) throw new Exception("Mirror lost independent aspect-fit framing.");
            var before = owner.GetTelemetry().CompositedUploads;
            mirror.SetWallVisibility(false); await Task.Delay(300);
            if (owner.GetTelemetry().CompositedUploads != before) throw new Exception("All-hidden shared source uploaded frames.");
            mirror.SetWallVisibility(true); await Task.Delay(200);
            if (owner.GetTelemetry().CompositedUploads <= before) throw new Exception("Revealed mirror did not resume uploads.");
            if (!await mirror.RefreshSnapshotAsync()) throw new Exception("Shared snapshot failed.");
            if (mirror.GetTelemetry().SnapshotCapturedAt is null || !File.Exists(Path.Combine(AppPaths.DataDirectory, "snapshots", "camera-33.jpg"))) throw new Exception("Shared snapshot used the wrong slot.");
            mirror.Dispose(); owner.SetWallVisibility(true); await Task.Delay(200);
            if (!player.IsPlaying) throw new Exception("Disposing a mirror stopped the owner.");
            var pendingCapture = owner.RefreshSnapshotAsync();
            owner.Dispose();
            await pendingCapture;
            typeof(CameraTile).GetMethod("ApplyVideoSizing", flags)!.Invoke(owner, new object?[] { player });
            if (owner.GetVideoDimensions() is not null) throw new Exception("Disposed source still accessed native dimensions.");
            Console.WriteLine("PASS shared decoder: one player, hidden-owner visible-mirror frames, independent aspect fit, all-hidden upload suppression, reveal, snapshots and mirror disposal");
        }
        finally { mirror.Dispose(); owner.Dispose(); window.Close(); }
    }
}
