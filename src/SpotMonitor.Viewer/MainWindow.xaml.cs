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
    private readonly CameraTile DoorbellTile = new();
    private CameraTile[] _tiles = [];
    private CameraTile[] _allTiles = [];
    private Window? _doorbellWindow;
    private bool _doorbellLayoutQueued;
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
        LocationChanged += (_, _) => QueueDoorbellLayout();
        SizeChanged += (_, _) => QueueDoorbellLayout();
        StateChanged += (_, _) => QueueDoorbellLayout();
        WallGrid.SizeChanged += (_, _) => QueueDoorbellLayout();
        _cursorTimer.Tick += (_, _) =>
        {
            CheckCornerGesture();
            if (_isFullScreen && _settings.HideMouseCursor &&
                DateTime.UtcNow - _lastMouseMovement >= TimeSpan.FromSeconds(_settings.MouseCursorHideSeconds))
            {
                if (Mouse.OverrideCursor is null) Mouse.OverrideCursor = System.Windows.Input.Cursors.None;
                foreach (var tile in _allTiles) tile.HideHoverControls();
            }
        };
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
        _settingsStore = new JsonSettingsStore(_settingsPath);
        _logger = new RollingFileLogger(Path.Combine(_dataDirectory, "logs"));
        _libVlc = new LibVLC("--no-video-title-show", "--no-osd");
        _libVlc.Log += (_, eventArgs) =>
        {
            var hardwareMatch = Regex.Match(eventArgs.FormattedLog, @"Using\s+(?<module>\S+)\s+\((?<device>.+?)(?:,\s+vendor\b|\)\s+for\s+hardware decoding)", RegexOptions.IgnoreCase);
            if (hardwareMatch.Success && eventArgs.FormattedLog.Contains("hardware decoding", StringComparison.OrdinalIgnoreCase))
                _hardwareDecoder = $"{hardwareMatch.Groups["module"].Value} • {hardwareMatch.Groups["device"].Value}";
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
            RefreshNativeVideoBackgrounds();
            if (_settings.DoorbellOverlay.Camera.Enabled)
                QueueDoorbellLayout();
            if (DateTime.UtcNow - _lastLanAddressRefresh >= TimeSpan.FromSeconds(30)) UpdateLanAddressText();
            foreach (var tile in _allTiles) tile.Tick(_hardwareDecoder);
            _telemetryPublisher.Publish(new ViewerTelemetry
            {
                ViewerUptimeSeconds = (long)_viewerUptime.Elapsed.TotalSeconds,
                ViewerMemoryMb = Math.Round(Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d, 1),
                HardwareDecoder = _hardwareDecoder,
                Cameras = _allTiles.Select(tile => tile.GetTelemetry()).ToArray()
            });
            await ReloadExternalConfigurationAsync();
        };
        _logger.Write("INFO", "Application startup: Phase 2 camera wall");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureDoorbellWindow();
        _tiles = [Tile1, Tile2, Tile3, Tile4, Tile5, Tile6, Tile7, Tile8, Tile9];
        _allTiles = [.. _tiles, DoorbellTile];
        foreach (var tile in _allTiles) tile.PointerActivity += Tile_PointerActivity;
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
        ApplyDoorbellOverlay();
        DoorbellTile.Initialize(_libVlc, _logger, _settings.DoorbellOverlay.Camera, _settings.RequestHardwareDecoding);
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
        QueueDoorbellLayout();
        RefreshNativeVideoBackgrounds(forceRedraw: true);
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, async () =>
        {
            await Task.Delay(250);
            QueueDoorbellLayout();
            RefreshNativeVideoBackgrounds(forceRedraw: true);
        });
    }

    private void RefreshNativeVideoBackgrounds(bool forceRedraw = false)
    {
        NativeVideoBackgroundGuard.Apply(forceRedraw);
        foreach (var tile in _allTiles) tile.EnsureNativeVideoBackground(forceRedraw);
    }

    private void ApplyDoorbellOverlay()
    {
        if (_doorbellWindow is null) return;
        foreach (var tile in _tiles)
        {
            tile.SetRestartButtonPlacement(RestartButtonPlacement.Center);
            tile.SetRestartButtonCompact(false);
        }
        DoorbellTile.SetRestartButtonPlacement(RestartButtonPlacement.Center);
        DoorbellTile.SetRestartButtonCompact(false);
        if (!_settings.DoorbellOverlay.Camera.Enabled)
        {
            _doorbellWindow.Hide();
            return;
        }
        _tiles[Math.Clamp(_settings.DoorbellOverlay.HostCameraSlot, 1, 9) - 1].SetRestartButtonCompact(true);
        UpdateDoorbellWindowLayout();
        QueueDoorbellLayout();
    }

    private void EnsureDoorbellWindow()
    {
        if (_doorbellWindow is not null) return;
        var window = new Window
        {
            Owner = this,
            Title = "SpotMonitor Doorbell Overlay",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = System.Windows.Media.Brushes.Black,
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(0),
            Content = DoorbellTile,
            Left = -32_000,
            Top = -32_000,
            Width = 1,
            Height = 1
        };
        window.SourceInitialized += (_, _) => ConfigureDoorbellWindow(window);
        _doorbellWindow = window;
    }

    private void QueueDoorbellLayout()
    {
        if (!IsLoaded || _doorbellWindow is null || _doorbellLayoutQueued) return;
        _doorbellLayoutQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _doorbellLayoutQueued = false;
            UpdateDoorbellWindowLayout();
        }));
    }

    private void UpdateDoorbellWindowLayout()
    {
        if (_doorbellWindow is null || _tiles.Length != 9) return;
        var overlay = _settings.DoorbellOverlay;
        if (!overlay.Camera.Enabled || !IsVisible || WindowState == WindowState.Minimized)
        {
            _doorbellWindow.Hide();
            return;
        }

        var target = _tiles[Math.Clamp(overlay.HostCameraSlot, 1, 9) - 1];
        if (target.ActualWidth <= 0 || target.ActualHeight <= 0) return;
        System.Windows.Point screenOrigin;
        try { screenOrigin = target.PointToScreen(new System.Windows.Point(0, 0)); }
        catch (InvalidOperationException) { return; }
        var dpi = VisualTreeHelper.GetDpi(target);
        var targetLeft = screenOrigin.X / dpi.DpiScaleX;
        var targetTop = screenOrigin.Y / dpi.DpiScaleY;
        var maximumWidth = Math.Max(1,
            target.ActualWidth * Math.Clamp(overlay.ViewportWidthPercent, 10, 95) / 100d);
        var maximumHeight = Math.Max(1,
            target.ActualHeight * Math.Clamp(overlay.ViewportHeightPercent, 10, 95) / 100d);
        var (width, height) = CalculateDoorbellViewportSize(overlay, maximumWidth, maximumHeight);
        var horizontalTravel = Math.Max(0, target.ActualWidth - width);
        var verticalTravel = Math.Max(0, target.ActualHeight - height);
        var left = targetLeft + horizontalTravel * overlay.ViewportHorizontalPositionPercent / 100d;
        var top = targetTop + verticalTravel * overlay.ViewportVerticalPositionPercent / 100d;

        _doorbellWindow.Left = Math.Round(left);
        _doorbellWindow.Top = Math.Round(top);
        _doorbellWindow.Width = Math.Round(width);
        _doorbellWindow.Height = Math.Round(height);
        DoorbellTile.ApplyVideoSizing(
            overlay.ZoomPercent,
            overlay.ImageHorizontalPositionPercent,
            overlay.ImageVerticalPositionPercent,
            width,
            height);
        DoorbellTile.ApplyViewportEdgeSmoothing(overlay.ViewportShape, width, height);
        _doorbellWindow.Topmost = _settings.KeepViewerAlwaysOnTop;
        if (!_doorbellWindow.IsVisible) _doorbellWindow.Show();
        ApplyDoorbellWindowRegion(overlay.ViewportShape, width, height, dpi);
        PlaceHostRestartButton(target, left - targetLeft, top - targetTop, width, height);
        BringDoorbellWindowToFront();
    }

    private static (double Width, double Height) CalculateDoorbellViewportSize(
        DoorbellOverlaySettings overlay,
        double maximumWidth,
        double maximumHeight)
    {
        if (overlay.ViewportShape is DoorbellViewportShape.Square or DoorbellViewportShape.Circle)
        {
            var side = Math.Max(1, Math.Min(maximumWidth, maximumHeight));
            return (side, side);
        }

        return (maximumWidth, maximumHeight);
    }

    private void ApplyDoorbellWindowRegion(DoorbellViewportShape shape, double width, double height, DpiScale dpi)
    {
        if (_doorbellWindow is null) return;
        var handle = new WindowInteropHelper(_doorbellWindow).Handle;
        if (handle == IntPtr.Zero) return;
        if (shape is DoorbellViewportShape.Native or DoorbellViewportShape.Square)
        {
            SetWindowRgn(handle, IntPtr.Zero, true);
            return;
        }

        var pixelWidth = Math.Max(1, (int)Math.Round(width * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Round(height * dpi.DpiScaleY));
        IntPtr region;
        if (shape is DoorbellViewportShape.Circle or DoorbellViewportShape.Oval)
            region = CreateEllipticRgn(0, 0, pixelWidth + 1, pixelHeight + 1);
        else
        {
            var aggressiveCornerDiameter = Math.Max(2, (int)Math.Round(Math.Min(pixelWidth, pixelHeight) * 0.44));
            region = CreateRoundRectRgn(0, 0, pixelWidth + 1, pixelHeight + 1,
                aggressiveCornerDiameter, aggressiveCornerDiameter);
        }

        if (region != IntPtr.Zero && SetWindowRgn(handle, region, true) == 0)
            DeleteObject(region);
    }

    private static void PlaceHostRestartButton(
        CameraTile target,
        double overlayLeft,
        double overlayTop,
        double overlayWidth,
        double overlayHeight)
    {
        const double buttonWidth = 24;
        const double buttonHeight = 24;
        const double margin = 0;
        var tileWidth = target.ActualWidth;
        var tileHeight = target.ActualHeight;
        var overlayBounds = new Rect(overlayLeft, overlayTop, overlayWidth, overlayHeight);
        var candidates = new[]
        {
            RestartCandidate(RestartButtonPlacement.Center, (tileWidth - buttonWidth) / 2, (tileHeight - buttonHeight) / 2),
            RestartCandidate(RestartButtonPlacement.TopCenter, (tileWidth - buttonWidth) / 2, margin),
            RestartCandidate(RestartButtonPlacement.BottomCenter, (tileWidth - buttonWidth) / 2, tileHeight - buttonHeight - margin),
            RestartCandidate(RestartButtonPlacement.CenterLeft, margin, (tileHeight - buttonHeight) / 2),
            RestartCandidate(RestartButtonPlacement.CenterRight, tileWidth - buttonWidth - margin, (tileHeight - buttonHeight) / 2),
            RestartCandidate(RestartButtonPlacement.TopLeft, margin, margin),
            RestartCandidate(RestartButtonPlacement.TopRight, tileWidth - buttonWidth - margin, margin),
            RestartCandidate(RestartButtonPlacement.BottomLeft, margin, tileHeight - buttonHeight - margin),
            RestartCandidate(RestartButtonPlacement.BottomRight, tileWidth - buttonWidth - margin, tileHeight - buttonHeight - margin)
        };
        var overlayCenter = new System.Windows.Point(
            overlayBounds.X + overlayBounds.Width / 2,
            overlayBounds.Y + overlayBounds.Height / 2);
        var clearCandidates = candidates.Where(candidate => !candidate.Bounds.IntersectsWith(overlayBounds)).ToArray();
        var best = (clearCandidates.Length > 0 ? clearCandidates : candidates)
            .OrderByDescending(candidate =>
                Math.Pow(candidate.Bounds.X + candidate.Bounds.Width / 2 - overlayCenter.X, 2) +
                Math.Pow(candidate.Bounds.Y + candidate.Bounds.Height / 2 - overlayCenter.Y, 2))
            .First();
        target.SetRestartButtonPlacement(best.Placement);

        (RestartButtonPlacement Placement, Rect Bounds) RestartCandidate(
            RestartButtonPlacement placement, double x, double y) =>
            (placement, new Rect(Math.Max(0, x), Math.Max(0, y),
                Math.Min(buttonWidth, tileWidth), Math.Min(buttonHeight, tileHeight)));
    }

    private static void ConfigureDoorbellWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(extendedStyle | WsExToolWindow | WsExNoActivate));
        var borderColor = DwmColorNone;
        DwmSetWindowAttribute(handle, DwmwaBorderColor, ref borderColor, sizeof(uint));
        var cornerPreference = DwmWindowCornerPreferenceDoNotRound;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(uint));
    }

    private void BringDoorbellWindowToFront()
    {
        if (_doorbellWindow?.IsVisible != true) return;
        var handle = new WindowInteropHelper(_doorbellWindow).Handle;
        if (handle == IntPtr.Zero) return;
        SetWindowPos(handle, _settings.KeepViewerAlwaysOnTop ? HwndTopmost : HwndTop, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    private void ApplyAlwaysOnTop()
    {
        Topmost = _settings.KeepViewerAlwaysOnTop;
        if (_doorbellWindow is not null) _doorbellWindow.Topmost = _settings.KeepViewerAlwaysOnTop;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        SetWindowPos(handle, _settings.KeepViewerAlwaysOnTop ? HwndTopmost : HwndNotTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        BringDoorbellWindowToFront();
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaWindowCornerPreference = 33;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const uint DwmWindowCornerPreferenceDoNotRound = 1;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref uint value, int valueSize);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) => RegisterPointerActivity();

    private void Tile_PointerActivity(object? sender, EventArgs e) => RegisterPointerActivity();

    private void RegisterPointerActivity()
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
            case ViewerCommandType.RestartCamera when command.Slot is >= 1 and <= 10 && command.Slot.Value <= _allTiles.Length:
                _allTiles[command.Slot.Value - 1].Start();
                var streamName = command.Slot == 10 ? "Doorbell" : $"Camera {command.Slot}";
                _logger.Write("INFO", $"Remote command: restarted {streamName}");
                return new ViewerCommandResult(command.Id, true, $"{streamName} restarted.");
            case ViewerCommandType.RestartAllCameras:
                foreach (var tile in _allTiles) tile.Start();
                _logger.Write("INFO", "Remote command: restarted all configured streams");
                return new ViewerCommandResult(command.Id, true, "All configured streams restarted.");
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
        ApplyDoorbellOverlay();
        DoorbellTile.Apply(_settings.DoorbellOverlay.Camera);
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
            var previousDoorbell = _settings.DoorbellOverlay.Camera;
            for (var index = 0; index < _tiles.Length; index++)
                if (_settings.Cameras[index] != updated.Cameras[index]) _tiles[index].Apply(updated.Cameras[index]);
            _settings = updated;
            ApplyDoorbellOverlay();
            if (previousDoorbell != updated.DoorbellOverlay.Camera)
                DoorbellTile.Apply(updated.DoorbellOverlay.Camera);
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
        foreach (var tile in _allTiles) tile.ApplyOverlayPreferences(_settings.ShowCameraNames, _settings.ShowCameraStats);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _diagnosticsTimer.Stop();
        _cursorTimer.Stop();
        Mouse.OverrideCursor = null;
        foreach (var tile in _allTiles) tile.Dispose();
        if (_doorbellWindow is not null)
        {
            _doorbellWindow.Content = null;
            _doorbellWindow.Close();
            _doorbellWindow = null;
        }
        _telemetryPublisher.Dispose();
        _commandServer.Dispose();
        _libVlc.Dispose();
        _logger.Write("INFO", "Application shutdown");
        base.OnClosing(e);
    }
}
