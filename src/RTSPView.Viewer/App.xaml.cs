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
        ViewerTaskbarIdentity.Initialize();
        base.OnStartup(e);
        _singleInstance = new Mutex(false, RTSPView.Infrastructure.ViewerRuntimeState.InstanceMutexName);
        try { _ownsSingleInstance = _singleInstance.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsSingleInstance = true; }
        if (!_ownsSingleInstance) { Shutdown(); return; }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var runtime = new RTSPView.Infrastructure.ViewerRuntimeState(RTSPView.Core.AppPaths.DataDirectory);
        var splash = new SplashWindow();
        splash.Show();
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                if (!await runtime.PrepareLaunchAsync(e.Args.Contains("--respect-viewer-pause"), timeout.Token)) { Shutdown(); return; }
            }
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
