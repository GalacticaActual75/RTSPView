using System.Windows;
using System.IO;
using LibVLCSharp.Shared;
using System.Windows.Threading;

namespace RTSPView.Viewer;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private bool _ownsSingleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "Local\\RTSPView.Viewer.SingleInstance", out var createdNew);
        _ownsSingleInstance = createdNew;
        if (!createdNew) { Shutdown(); return; }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var splash = new SplashWindow();
        splash.Show();
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            splash.SetStatus("Loading native video engine…");
            var bundledLibVlc = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
            LibVLCSharp.Shared.Core.Initialize(Directory.Exists(bundledLibVlc) ? bundledLibVlc : null);
            await Dispatcher.Yield(DispatcherPriority.Background);

            splash.SetStatus("Preparing camera players…");
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.ContentRendered += (_, _) => splash.Close();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            splash.ShowFailure($"RTSPView could not start.\n\n{exception.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstance) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
