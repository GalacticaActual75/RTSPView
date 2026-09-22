using System.Diagnostics;
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

// Local decoding/presentation benchmark; no camera connections or hardware capacity claim.
internal static class CapacityChecks
{
    public static async Task Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"RTSPView-capacity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var file=Path.Combine(root,"fixture.y4m");
        using(var output=File.Create(file)) {
            output.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W640 H360 F30:1 Ip A1:1 C420jpeg\n"));
            var pixels=Enumerable.Repeat((byte)128,640*360*3/2).ToArray();
            for(var i=0;i<450;i++){output.Write(Encoding.ASCII.GetBytes("FRAME\n"));output.Write(pixels);}
        }
        using var engine=new LibVLC("--no-video-title-show","--no-osd","--no-audio","--no-snapshot-preview");
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var rows=new List<string>{"main,overlays,overlay_visible,views,players,advancing_views,hidden,uploads,hidden_uploads,cpu_ms,working_set_mb"};
        foreach(var main in new[]{1,9,16})foreach(var overlays in new[]{0,2,16})foreach(var visible in new[]{false,true}) {
            var grid=new Grid();var tiles=new List<CameraTile>();
            var window=new Window{Content=grid,Width=640,Height=360,Left=-20000,Top=-20000,ShowInTaskbar=false,ShowActivated=false};
            try {
                for(var i=0;i<main+overlays*2;i++) {
                    var tile=new CameraTile();tiles.Add(tile);grid.Children.Add(tile);
                    tile.Initialize(engine,new RollingFileLogger(Path.Combine(root,"logs")),new CameraSettings{Slot=i+1,Enabled=true},false,compositedVideo:true,preserveWholeFrame:i>=main+overlays,sharedSource:i>=main+overlays?tiles[i-overlays]:null);
                }
                window.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                for(var i=0;i<tiles.Count;i++) {
                    var tile=tiles[i];tile.SetWallVisibility(i<main||i<main+overlays&&visible);
                    if (!tile.OwnsDecoder) continue;
                    var player=(MediaPlayer)typeof(CameraTile).GetField("_player",flags)!.GetValue(tile)!;
                    var vlc=(LibVLC)typeof(CameraTile).GetField("_libVlc",flags)!.GetValue(tile)!;
                    var media=new Media(vlc,new Uri(file));media.AddOption(":avcodec-hw=none");media.AddOption(":no-audio");
                    typeof(CameraTile).GetField("_media",flags)!.SetValue(tile,media);
                    if(!player.Play(media))throw new Exception("Capacity fixture did not start");
                }
                for(var attempt=0;attempt<60;attempt++){await Task.Delay(100);tiles.ForEach(t=>t.Tick());if(tiles.All(t=>t.GetTelemetry().LastFrameAt is not null))break;}
                var initial=tiles.Select(t=>t.GetTelemetry()).ToArray();using var process=Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;
                await Task.Delay(1000);tiles.ForEach(t=>t.Tick());process.Refresh();
                var final=tiles.Select(t=>t.GetTelemetry()).ToArray();var decoding=final.Zip(initial).Count(p=>p.First.LastFrameAt>p.Second.LastFrameAt);
                var hiddenUploads=final.Zip(initial).Where(p=>!p.First.Visible).Sum(p=>p.First.CompositedUploads-p.Second.CompositedUploads);
                var uploads=final.Zip(initial).Sum(p=>p.First.CompositedUploads-p.Second.CompositedUploads);
                if(decoding!=tiles.Count||hiddenUploads!=0||uploads==0)throw new Exception($"Capacity regression: {main}/{overlays}/{visible}, decoding {decoding}/{tiles.Count}, hidden uploads {hiddenUploads}");
                rows.Add($"{main},{overlays},{visible},{tiles.Count},{tiles.Count(t=>t.OwnsDecoder)},{decoding},{final.Count(t=>!t.Visible)},{uploads},{hiddenUploads},{(process.TotalProcessorTime-cpu).TotalMilliseconds:F0},{process.WorkingSet64/1048576d:F1}");
                Console.WriteLine("PASS presentation capacity "+rows[^1]);
            } finally {foreach(var tile in tiles.OrderBy(t=>t.OwnsDecoder))tile.Dispose();window.Close();}
        }
        var target=Path.GetFullPath("artifacts/capacity");Directory.CreateDirectory(target);File.WriteAllLines(Path.Combine(target,"shared-software-640x360.csv"),rows);
    }
}
