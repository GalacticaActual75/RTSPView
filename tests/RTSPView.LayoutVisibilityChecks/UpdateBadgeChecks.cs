using System.IO;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RTSPView.Core;
using RTSPView.Viewer;

internal static class UpdateBadgeChecks
{
    public static async Task Run(Window owner)
    {
        var thermal = new TemperatureWarningWindow(owner) { Left = -20000, Top = -20000 };
        try
        {
            var now = DateTimeOffset.UtcNow;
            var status = new TemperatureStatus { Timestamp = now, CpuC = 96, GpuC = 91,
                Settings = new() { ShowWarnings = true, CpuWarningEnabled = true, GpuWarningEnabled = true } };
            if (!thermal.SetStatus(status, now)) throw new Exception("Hot sensors did not warn");
            thermal.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var panel = (Border)thermal.Content;
            var text = ((StackPanel)panel.Child).Children.OfType<TextBlock>().Last();
            if (((SolidColorBrush)panel.Background).Color.A < 210 || !text.Text.Contains("GPU") || text.FontSize < 24) throw new Exception("Thermal warning readability");
            Capture(panel, "temperature-warning.png");
            thermal.PlaceWithin(new Point(-20000, -20000), new Size(1200, 800));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (Math.Abs(thermal.Left + thermal.ActualWidth / 2 - (-19400)) > 1 ||
                Math.Abs(thermal.Top + thermal.ActualHeight / 2 - (-19600)) > 1)
                throw new Exception("Temperature warning is not centered");
            var initialColor = ((SolidColorBrush)panel.Background).Color;
            var flashed = false;
            for (var frame = 0; frame < 12; frame++)
            {
                await Task.Delay(100);
                if (((SolidColorBrush)panel.Background).Color != initialColor) { flashed = true; break; }
            }
            if (!flashed) throw new Exception("Temperature warning did not visibly flash");
            using (var layers = new TestOverlayLayers(owner))
            {
                var raise = typeof(MainWindow).GetMethod("RaiseWindow", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                raise.Invoke(null, [layers.Video]); raise.Invoke(null, [layers.Status]); raise.Invoke(null, [thermal]);
                var warningHandle = new System.Windows.Interop.WindowInteropHelper(thermal).Handle;
                foreach (var layer in new[] { layers.Video, layers.Status })
                {
                    var current = new System.Windows.Interop.WindowInteropHelper(layer).Handle;
                    var above = false;
                    for (var count = 0; count < 1000 && current != IntPtr.Zero; count++)
                    { current = GetWindow(current, 3); if (current == warningHandle) { above = true; break; } }
                    if (!above) throw new Exception("Temperature warning remained below an overlay window");
                }
            }
            if (thermal.SetStatus(status with { Settings = status.Settings with { ShowWarnings = false } }, now) || thermal.IsVisible)
                throw new Exception("Master temperature toggle did not hide warning");
            if (thermal.SetStatus(status, now.AddSeconds(21))) throw new Exception("Stale temperature warning remained");
            Console.WriteLine("PASS centered, visibly flashing temperature warning, master off and stale reading dismissal.");
        }
        finally { thermal.Close(); }
        // Render the shipped bottom bar without wiring any real browser/stream actions.
        var xaml=System.Xml.Linq.XDocument.Load(Path.Combine(Directory.GetCurrentDirectory(),"src","RTSPView.Viewer","MainWindow.xaml"));
        var bar=xaml.Descendants().First(e=>e.Name.LocalName=="Border"&&e.Attributes().Any(a=>a.Name.LocalName=="Name"&&a.Value=="ControlBar"));
        foreach(var attribute in bar.DescendantsAndSelf().Attributes().Where(a=>a.Name.LocalName is "Click" or "SelectionChanged").ToArray())attribute.Remove();
        var control=(Border)System.Windows.Markup.XamlReader.Parse(bar.ToString());
        foreach(var width in new[]{900,1600})
        {
            control.Width=width;control.Measure(new Size(width,double.PositiveInfinity));control.Arrange(new Rect(0,0,width,control.DesiredSize.Height));control.UpdateLayout();
            Capture(control,$"viewer-controlbar-{width}.png");
            var dock=(DockPanel)control.Child;
            var content=dock.Children.OfType<WrapPanel>().Single();
            var web=content.Children.OfType<Button>().Single(b=>b.Content?.ToString()=="Open settings");
            var point=web.TranslatePoint(new Point(),control);
            if(point.X<0||point.X+web.ActualWidth>width||point.Y+web.ActualHeight>control.ActualHeight)throw new Exception("Web configuration button clipped");
            var exit=dock.Children.OfType<Button>().Single(b=>b.Content?.ToString()=="Full exit");
            var exitPoint=exit.TranslatePoint(new Point(),control);
            if(exitPoint.X<point.X+web.ActualWidth||exitPoint.X+exit.ActualWidth>width||exitPoint.Y+exit.ActualHeight>control.ActualHeight)
                throw new Exception("Full exit overlaps controls or is clipped");
        }
        var calls=0;
        var badge=new UpdateBadgeWindow(owner,request=>
        {
            if(!request.Confirmed||request.Version!="1.0.40-beta.5"||request.Channel!="beta")throw new Exception("Wrong confirmed update request");
            calls++;return Task.FromResult(new WallUpdateResponse(true,"Fake installer accepted"));
        }) {Left=-20000,Top=-20000};
        try
        {
            badge.SetNotice(new(true,"1.0.40-beta.5","beta"));badge.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var border=(Border)badge.Content;var button=(Button)border.Child;
            if(((SolidColorBrush)border.Background).Color.A<210||badge.Owner!=owner||badge.ShowInTaskbar)throw new Exception("Badge readability/ownership regression");
            Capture(border,"update-badge.png");
            await Click(false);if(calls!=0)throw new Exception("Cancel started installer");
            await Click(true);if(calls!=1||badge.IsVisible)throw new Exception("Confirmed badge update did not start exactly once and hide");
            Console.WriteLine("PASS smoked badge, accessible confirmation, cancellation and exact-version installation using fake installer.");

            async Task Click(bool accept)
            {
                Exception? failure=null;
                var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
                timer.Tick+=(_,_)=>
                {
                    var dialog=owner.OwnedWindows.Cast<Window>().FirstOrDefault(w=>w.Title=="Install RTSPView update");
                    if(dialog is null)return;
                    timer.Stop();
                    try
                    {
                        var panel=(StackPanel)dialog.Content;
                        var buttons=(StackPanel)panel.Children[panel.Children.Count-1];
                        if(((Button)buttons.Children[0]).Content.ToString()!="_Cancel"||((Button)buttons.Children[1]).Content.ToString()!="_Install")throw new Exception("Confirmation actions missing");
                        Capture(dialog,"update-confirmation.png");
                        if(accept)((Button)buttons.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        else dialog.DialogResult=false;
                    }
                    catch(Exception error){failure=error;dialog.DialogResult=false;}
                };
                timer.Start();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);timer.Stop();
                if(failure is not null)throw failure;
            }
        }
        finally{badge.Close();}
    }
    private static void Capture(FrameworkElement visual,string name)
    {
        visual.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);
        var drawing=new DrawingVisual();
        using(var context=drawing.RenderOpen())context.DrawRectangle(new VisualBrush(visual),null,new Rect(0,0,visual.ActualWidth,visual.ActualHeight));
        bitmap.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","ui-qa");Directory.CreateDirectory(directory);
        using var file=File.Create(Path.Combine(directory,name));encoder.Save(file);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    private sealed class TestOverlayLayers : IDisposable
    {
        public Window Video { get; }
        public Window Status { get; }
        public TestOverlayLayers(Window owner)
        {
            Video = new Window { Owner = owner, Left = -20000, Top = -20000, Width = 500, Height = 200, ShowActivated = false, ShowInTaskbar = false };
            Video.Show();
            Status = new Window { Owner = Video, Left = -20000, Top = -20000, Width = 500, Height = 100, ShowActivated = false, ShowInTaskbar = false };
            Status.Show();
        }
        public void Dispose() { Status.Close(); Video.Close(); }
    }
}
