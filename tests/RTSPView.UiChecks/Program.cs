using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RTSPView.Core;
using RTSPView.Infrastructure;
using RTSPView.Viewer;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SpotMonitor.Viewer;component/ProductTheme.xaml", UriKind.Relative) });
        var directory = Path.Combine(Path.GetTempPath(), "RTSPView-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settings = new AppSettings();
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
        var window = new ConfigurationWindow(settings, store) { Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            window.Show();window.UpdateLayout();
            var list = (ListBox)window.FindName("SlotBox");
            var name = (TextBox)window.FindName("NameBox");
            var url = (TextBox)window.FindName("UrlBox");
            name.Text = "UI draft";url.Text = "rtsp://example.test/live";
            ((CheckBox)window.FindName("CompositeBox")).IsChecked = true;
            list.SelectedIndex = 1;list.SelectedIndex = 0;
            if(name.Text != "UI draft" || url.Text != "rtsp://example.test/live")throw new Exception("Switching streams lost a draft.");
            if(((CheckBox)window.FindName("CompositeBox")).IsChecked != true)throw new Exception("Composite option was lost.");
            if(File.Exists(Path.Combine(directory,"settings.json")))throw new Exception("Editing unexpectedly persisted settings.");
            if(list.Items.Count != settings.CameraCount)throw new Exception("Stream list count differs from configuration.");
            window.Width=700;window.Height=550;window.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
            var output=Path.GetFullPath("artifacts/beta-redesign");Directory.CreateDirectory(output);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(Path.Combine(output,"native-stream-editor.png")))encoder.Save(file);
            Console.WriteLine("PASS native editor: resources, minimum window size, draft preservation, stream selection, composite option, no premature persistence.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{window.Close();app.Shutdown();}
    }
}
