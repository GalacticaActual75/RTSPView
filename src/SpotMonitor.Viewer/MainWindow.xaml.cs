using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using SpotMonitor.Core;
using SpotMonitor.Infrastructure;

namespace SpotMonitor.Viewer;

public partial class MainWindow : Window
{
    private readonly string _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpotMonitor");
    private readonly string _settingsPath;
    private readonly DispatcherTimer _diagnosticsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly LibVLC _libVlc;
    private readonly JsonSettingsStore _settingsStore;
    private readonly RollingFileLogger _logger;
    private CameraTile[] _tiles = [];
    private AppSettings _settings = new();
    private string _hardwareDecoder = "HW requested";
    private DateTime _settingsLastWriteUtc;
    private bool _reloadInProgress;
    private readonly ViewerTelemetryPublisher _telemetryPublisher = new();
    private readonly ViewerCommandServer _commandServer;
    private readonly Stopwatch _viewerUptime = Stopwatch.StartNew();
    private readonly DispatcherTimer _cursorTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private DateTime _lastMouseMovement = DateTime.UtcNow;
    private DateTime _lastLanAddressRefresh = DateTime.MinValue;
    private bool _isFullScreen;
    private DateTime _cornerClickWindowStarted;
    private int _cornerClickCount;
    private bool _leftButtonWasDown;

    public MainWindow()
    {
        _commandServer = new ViewerCommandServer(HandleCommandAsync);
        InitializeComponent();
        _cursorTimer.Tick += (_, _) =>
        {
            CheckCornerGesture();
            if (_isFullScreen && _settings.HideMouseCursor && DateTime.UtcNow - _lastMouseMovement >= TimeSpan.FromSeconds(_settings.MouseCursorHideSeconds))
                Mouse.OverrideCursor = System.Windows.Input.Cursors.None;
        };
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
        _settingsStore = new JsonSettingsStore(_settingsPath);
        _logger = new RollingFileLogger(Path.Combine(_dataDirectory, "logs"));
        _libVlc = new LibVLC("--no-video-title-show", "--no-osd");
        _libVlc.Log += (_, eventArgs) =>
        {
            var hardwareMatch = Regex.Match(eventArgs.FormattedLog, @"Using\s+([^\s]+)\s+\(([^,\)]+)", RegexOptions.IgnoreCase);
            if (hardwareMatch.Success && eventArgs.FormattedLog.Contains("hardware decoding", StringComparison.OrdinalIgnoreCase))
                _hardwareDecoder = $"{hardwareMatch.Groups[1].Value} • {hardwareMatch.Groups[2].Value}";
            else if (eventArgs.FormattedLog.Contains("using hw decoder module", StringComparison.OrdinalIgnoreCase) && _hardwareDecoder == "HW requested")
                _hardwareDecoder = "Hardware decode active";
            if (eventArgs.Level >= LogLevel.Warning)
            {
                // LibVLC emits late-picture messages from its shared engine without
                // media-player context. CameraTile logs the corresponding lost-frame
                // counters with the camera slot and name, which is the useful signal.
                if (eventArgs.FormattedLog.Contains("picture is too late to be displayed", StringComparison.OrdinalIgnoreCase)) return;
                _logger.Write($"VLC-{eventArgs.Level}", $"Global LibVLC event (camera unavailable): {eventArgs.FormattedLog}");
            }
        };
        Loaded += OnLoaded;
        _diagnosticsTimer.Tick += async (_, _) =>
        {
            if (_settings.KeepViewerAlwaysOnTop) ApplyAlwaysOnTop();
            NativeVideoBackgroundGuard.Apply();
            if (DateTime.UtcNow - _lastLanAddressRefresh >= TimeSpan.FromSeconds(30)) UpdateLanAddressText();
            foreach (var tile in _tiles) tile.Tick(_hardwareDecoder);
            _telemetryPublisher.Publish(new ViewerTelemetry
            {
                ViewerUptimeSeconds = (long)_viewerUptime.Elapsed.TotalSeconds,
                ViewerMemoryMb = Math.Round(Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d, 1),
                HardwareDecoder = _hardwareDecoder,
                Cameras = _tiles.Select(tile => tile.GetTelemetry()).ToArray()
            });
            await ReloadExternalConfigurationAsync();
        };
        _logger.Write("INFO", "Application startup: Phase 2 camera wall");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _tiles = [Tile1, Tile2, Tile3, Tile4, Tile5, Tile6, Tile7, Tile8, Tile9];
        _settings = (await _settingsStore.LoadAsync()).Normalize();
        _settingsLastWriteUtc = File.Exists(_settingsPath) ? File.GetLastWriteTimeUtc(_settingsPath) : DateTime.MinValue;
        var commandLineUrl = ReadArgument("--rtsp");
        var fillAll = Environment.GetCommandLineArgs().Any(value => value.Equals("--fill-all", StringComparison.OrdinalIgnoreCase));
        if (commandLineUrl is not null)
        {
            var cameras = _settings.Cameras.ToArray();
            var slots = fillAll ? Enumerable.Range(0, 9) : [0];
            foreach (var index in slots) cameras[index] = cameras[index] with { RtspUrl = commandLineUrl, Enabled = true };
            _settings = _settings with { Cameras = cameras };
        }
        var cameraArguments = ReadCameraArguments();
        if (cameraArguments.Count > 0)
        {
            var cameras = _settings.Cameras.ToArray();
            foreach (var (slot, url) in cameraArguments) cameras[slot - 1] = cameras[slot - 1] with { RtspUrl = url, Enabled = true };
            _settings = _settings with { Cameras = cameras };
        }
        for (var index = 0; index < 9; index++) _tiles[index].Initialize(_libVlc, _logger, _settings.Cameras[index], _settings.RequestHardwareDecoding);
        ApplyOverlayPreferences();
        LoadEditor(0);
        UpdateLanAddressText();
        PositionOnPreferredMonitor();
        SetFullScreen(_settings.StartFullScreen);
        _cursorTimer.Start();
        _diagnosticsTimer.Start();
    }

    private static string? ReadArgument(string name)
    {
        var arguments = Environment.GetCommandLineArgs();
        for (var index = 1; index < arguments.Length - 1; index++)
            if (arguments[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return arguments[index + 1];
        return null;
    }

    private static IReadOnlyDictionary<int, string> ReadCameraArguments()
    {
        var result = new Dictionary<int, string>();
        var arguments = Environment.GetCommandLineArgs();
        for (var slot = 1; slot <= 9; slot++)
        {
            var name = $"--camera{slot}";
            for (var index = 1; index < arguments.Length - 1; index++)
                if (arguments[index].Equals(name, StringComparison.OrdinalIgnoreCase)) result[slot] = arguments[index + 1];
        }
        return result;
    }

    private void SlotBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) LoadEditor(Math.Max(0, SlotBox.SelectedIndex));
    }

    private void LoadEditor(int index)
    {
        var camera = _settings.Cameras[index];
        UrlBox.Text = camera.RtspUrl;
        TransportBox.SelectedIndex = (int)camera.Transport;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var index = Math.Max(0, SlotBox.SelectedIndex);
        var cameras = _settings.Cameras.ToArray();
        cameras[index] = cameras[index] with { RtspUrl = UrlBox.Text.Trim(), Transport = (RtspTransport)Math.Max(0, TransportBox.SelectedIndex), Enabled = true };
        _settings = _settings with { Cameras = cameras };
        await _settingsStore.SaveAsync(_settings);
        _settingsLastWriteUtc = File.GetLastWriteTimeUtc(_settingsPath);
        _tiles[index].Apply(cameras[index]);
        _logger.Write("INFO", $"Camera {index + 1} configuration changed: {RtspUrlSanitizer.Redact(cameras[index].RtspUrl)}");
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e) => _tiles[Math.Max(0, SlotBox.SelectedIndex)].Start();

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        SetFullScreen(true);
        _logger.Write("INFO", "Local control: entered full screen");
    }

    private void UpdateLanAddressText()
    {
        string[] addresses;
        try
        {
            addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                .Select(address => address.Address.ToString())
                .Distinct()
                .OrderBy(address => address, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            addresses = [];
        }

        var value = addresses.Length == 0 ? "unavailable" : string.Join(", ", addresses);
        LanAddressText.Text = $"LAN IP: {value}";
        LanAddressText.ToolTip = LanAddressText.Text;
        _lastLanAddressRefresh = DateTime.UtcNow;
    }

    private void PositionOnPreferredMonitor()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var index = Math.Clamp(_settings.PreferredMonitor, 0, Math.Max(0, screens.Length - 1));
        var bounds = screens[index].Bounds;
        var dpi = VisualTreeHelper.GetDpi(this);
        WindowState = WindowState.Normal;
        Left = bounds.Left / dpi.DpiScaleX;
        Top = bounds.Top / dpi.DpiScaleY;
        Width = bounds.Width / dpi.DpiScaleX;
        Height = bounds.Height / dpi.DpiScaleY;
    }

    private void SetFullScreen(bool enabled)
    {
        _isFullScreen = enabled;
        if (enabled)
        {
            PositionOnPreferredMonitor();
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ControlBar.Visibility = Visibility.Collapsed;
            WindowState = WindowState.Maximized;
            Activate();
        }
        else
        {
            Mouse.OverrideCursor = null;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            ControlBar.Visibility = Visibility.Visible;
        }
        ApplyAlwaysOnTop();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => NativeVideoBackgroundGuard.Apply(forceRedraw: true));
    }

    private void ApplyAlwaysOnTop()
    {
        Topmost = _settings.KeepViewerAlwaysOnTop;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        SetWindowPos(handle, _settings.KeepViewerAlwaysOnTop ? HwndTopmost : HwndNotTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _lastMouseMovement = DateTime.UtcNow;
        if (Mouse.OverrideCursor is not null) Mouse.OverrideCursor = null;
    }

    private void CheckCornerGesture()
    {
        var leftButtonDown = System.Windows.Forms.Control.MouseButtons.HasFlag(System.Windows.Forms.MouseButtons.Left);
        var newClick = leftButtonDown && !_leftButtonWasDown;
        _leftButtonWasDown = leftButtonDown;
        if (!_isFullScreen || !newClick) return;
        var screens = System.Windows.Forms.Screen.AllScreens;
        var index = Math.Clamp(_settings.PreferredMonitor, 0, Math.Max(0, screens.Length - 1));
        var bounds = screens[index].Bounds;
        var position = System.Windows.Forms.Cursor.Position;
        if (position.X < bounds.Right - 64 || position.X >= bounds.Right || position.Y < bounds.Top || position.Y >= bounds.Top + 64) return;
        var now = DateTime.UtcNow;
        if (now - _cornerClickWindowStarted > TimeSpan.FromSeconds(3))
        {
            _cornerClickWindowStarted = now;
            _cornerClickCount = 0;
        }
        _logger.Write("INFO", $"Appliance escape corner click {++_cornerClickCount}/5");
        if (_cornerClickCount < 5) return;
        _cornerClickCount = 0;
        if (System.Windows.MessageBox.Show(this, "Leave SpotMonitor full-screen appliance mode?", "SpotMonitor", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            SetFullScreen(false);
    }

    private Task<ViewerCommandResult> HandleCommandAsync(ViewerCommand command) => Dispatcher.InvokeAsync(() =>
    {
        switch (command.Type)
        {
            case ViewerCommandType.RestartCamera when command.Slot is >= 1 and <= 9:
                _tiles[command.Slot.Value - 1].Start();
                _logger.Write("INFO", $"Remote command: restarted camera {command.Slot}");
                return new ViewerCommandResult(command.Id, true, $"Camera {command.Slot} restarted.");
            case ViewerCommandType.RestartAllCameras:
                foreach (var tile in _tiles) tile.Start();
                _logger.Write("INFO", "Remote command: restarted all cameras");
                return new ViewerCommandResult(command.Id, true, "All cameras restarted.");
            case ViewerCommandType.EnterFullScreen:
                SetFullScreen(true);
                _logger.Write("INFO", "Remote command: entered full screen");
                return new ViewerCommandResult(command.Id, true, "Full-screen mode enabled.");
            case ViewerCommandType.ExitFullScreen:
                SetFullScreen(false);
                _logger.Write("INFO", "Remote command: exited full screen");
                return new ViewerCommandResult(command.Id, true, "Full-screen mode disabled.");
            case ViewerCommandType.RestartViewer:
                var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Viewer executable path is unavailable.");
                var escapedExecutable = executable.Replace("'", "''");
                var restartHelper = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
                restartHelper.ArgumentList.Add("-NoProfile");
                restartHelper.ArgumentList.Add("-WindowStyle");
                restartHelper.ArgumentList.Add("Hidden");
                restartHelper.ArgumentList.Add("-Command");
                restartHelper.ArgumentList.Add($"Start-Sleep -Seconds 2; Start-Process -FilePath '{escapedExecutable}'");
                Process.Start(restartHelper);
                _logger.Write("INFO", "Remote command: restarting viewer");
                Dispatcher.BeginInvoke(System.Windows.Application.Current.Shutdown);
                return new ViewerCommandResult(command.Id, true, "Viewer restart started.");
            default:
                return new ViewerCommandResult(command.Id, false, "Invalid viewer command or camera slot.");
        }
    }).Task;

    private void ConfigureButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfigurationWindow(_settings, _settingsStore) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _settings = dialog.Settings.Normalize();
        _settingsLastWriteUtc = File.GetLastWriteTimeUtc(_settingsPath);
        for (var index = 0; index < _tiles.Length; index++) _tiles[index].Apply(_settings.Cameras[index]);
        ApplyOverlayPreferences();
        LoadEditor(Math.Max(0, SlotBox.SelectedIndex));
        _logger.Write("INFO", "Configuration saved and applied to all changed camera slots");
    }

    private async Task ReloadExternalConfigurationAsync()
    {
        if (_reloadInProgress || !File.Exists(_settingsPath)) return;
        var writeTime = File.GetLastWriteTimeUtc(_settingsPath);
        if (writeTime <= _settingsLastWriteUtc) return;
        _reloadInProgress = true;
        try
        {
            var updated = (await _settingsStore.LoadAsync()).Normalize();
            for (var index = 0; index < _tiles.Length; index++)
                if (_settings.Cameras[index] != updated.Cameras[index]) _tiles[index].Apply(updated.Cameras[index]);
            _settings = updated;
            ApplyOverlayPreferences();
            PositionOnPreferredMonitor();
            SetFullScreen(updated.StartFullScreen);
            _settingsLastWriteUtc = writeTime;
            LoadEditor(Math.Max(0, SlotBox.SelectedIndex));
            _logger.Write("INFO", "External configuration change applied live");
        }
        catch (Exception exception) { _logger.Write("ERROR", $"External configuration reload failed: {exception.Message}"); }
        finally { _reloadInProgress = false; }
    }

    private void ApplyOverlayPreferences()
    {
        foreach (var tile in _tiles) tile.ApplyOverlayPreferences(_settings.ShowCameraNames, _settings.ShowCameraStats);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _diagnosticsTimer.Stop();
        _cursorTimer.Stop();
        Mouse.OverrideCursor = null;
        foreach (var tile in _tiles) tile.Dispose();
        _telemetryPublisher.Dispose();
        _commandServer.Dispose();
        _libVlc.Dispose();
        _logger.Write("INFO", "Application shutdown");
        base.OnClosing(e);
    }
}
