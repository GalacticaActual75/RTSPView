using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using RTSPView.Core;
using RTSPView.Infrastructure;
using RTSPView.Viewer;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ViewerTaskbarIdentity.Initialize();
        Marshal.ThrowExceptionForHR(GetCurrentProcessExplicitAppUserModelID(out var appId));
        try
        {
            if (Marshal.PtrToStringUni(appId) != ViewerTaskbarIdentity.AppId)
                throw new Exception("Windows did not retain the viewer taskbar identity.");
        }
        finally { Marshal.FreeCoTaskMem(appId); }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/{typeof(CameraTile).Assembly.GetName().Name};component/ProductTheme.xaml", UriKind.Relative) });
        var directory = Path.Combine(Path.GetTempPath(), "RTSPView-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("RTSPVIEW_DATA_DIR", directory);
        var settings = new AppSettings();
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
        var window = new ConfigurationWindow(settings, store) { Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            var viewer = new MainWindow { Left = -20000, Top = -20000, ShowActivated = false };
            try
            {
                // Create the real viewer HWND without starting camera playback.
                new WindowInteropHelper(viewer).EnsureHandle();
            SensorViewerChecks.Run(viewer);
            HostOverlayChecks.Run();
                CheckWindowIcon(viewer);
                viewer.WindowStyle = WindowStyle.None;
                viewer.ResizeMode = ResizeMode.NoResize;
                CheckWindowIcon(viewer);
                viewer.WindowState = WindowState.Minimized;
                CheckWindowIcon(viewer);
                viewer.WindowState = WindowState.Normal;
                viewer.WindowStyle = WindowStyle.SingleBorderWindow;
                CheckWindowIcon(viewer);
                Console.WriteLine("PASS main viewer native small/large icons through borderless, minimize and restore.");
                if (viewer.FindName("FullExitButton") is not Button exit || exit.Content?.ToString() != "Full exit")
                    throw new Exception("Windowed viewer is missing Full exit.");
                typeof(MainWindow).GetField("_exitingIntentionally", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(viewer, true);
                var commandHandler = typeof(MainWindow).GetMethod("HandleCommandOnUiAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var restart = new ViewerCommand(Guid.NewGuid(), ViewerCommandType.RestartViewer);
                var refusal = ((Task<ViewerCommandResult>)commandHandler.Invoke(viewer, [restart])!).GetAwaiter().GetResult();
                if (refusal.Success || !refusal.ExitingIntentionally)
                    throw new Exception("Queued watchdog restart was not told to yield to intentional exit.");
                var closed = false;
                viewer.Closed += (_, _) => closed = true;
                var stop = typeof(MainWindow).GetMethod("PauseRecoveryAndCloseAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                ((Task)stop.Invoke(viewer, null)!).GetAwaiter().GetResult();
                var runtime = new ViewerRuntimeState(directory);
                if (!closed || !runtime.Paused) throw new Exception("Full exit did not close the viewer with recovery paused.");
                runtime.PrepareLaunchAsync(false, CancellationToken.None).GetAwaiter().GetResult();
                if (runtime.Paused) throw new Exception("Manual launch did not restore automatic recovery.");
                Console.WriteLine("PASS actual viewer exit closes its native window, persists recovery pause, and manual launch clears it.");
            }
            finally { viewer.Close(); }
            var splash = new SplashWindow { Left = -20000, Top = -20000, Topmost = false, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
            try
            {
                splash.Show();
                CheckWindowIcon(splash);
                splash.WindowState = WindowState.Minimized;
                CheckWindowIcon(splash);
                splash.WindowState = WindowState.Normal;
                CheckWindowIcon(splash);
            }
            finally { splash.Close(); }
            window.Show();window.UpdateLayout();
            CheckWindowIcon(window);
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

    private static void CheckWindowIcon(Window window)
    {
        if (!window.Title.StartsWith("RTSPView", StringComparison.Ordinal) || window.Icon is null)
            throw new Exception("Taskbar-visible window lacks RTSPView branding.");
        var handle = new WindowInteropHelper(window).Handle;
        // Check actual Win32 icons, rather than just the XAML property.
        if (SendMessage(handle, 0x007F, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero ||
            SendMessage(handle, 0x007F, new IntPtr(1), IntPtr.Zero) == IntPtr.Zero)
            throw new Exception("RTSPView small or large native window icon is missing.");
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll")]
    private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr appId);
}
