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
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Viewer;

public partial class MainWindow : Window
{
    private readonly string _dataDirectory = AppPaths.DataDirectory;
    private readonly string _settingsPath;
    private readonly DispatcherTimer _diagnosticsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly LibVLC _libVlc;
    private readonly JsonSettingsStore _settingsStore;
    private readonly RollingFileLogger _logger;
    private readonly CameraTile DoorbellTile = new();
    private readonly CameraTile GarageTile = new();
    private CameraTile[] _tiles = [];
    private CameraTile[] _allTiles = [];
    private Window? _doorbellWindow;
    private Window? _garageWindow;
    private readonly Dictionary<int, (CameraTile Tile, Window Window, CameraSettings Camera)> _additionalOverlays = new();

    private void SyncAdditionalOverlays()
    {
        var wanted = _settings.AdditionalOverlays.Select(overlay => overlay.Camera.Slot).ToHashSet();
        foreach (var slot in _additionalOverlays.Keys.Where(slot => !wanted.Contains(slot)).ToArray())
        {
            var removed = _additionalOverlays[slot];
            HideOverlayWindowHierarchy(removed.Window, removed.Tile);
            removed.Tile.PointerActivity -= Tile_PointerActivity;
            removed.Tile.Dispose();
            removed.Window.Content = null;
            removed.Window.Close();
            _additionalOverlays.Remove(slot);
        }
        foreach (var overlay in _settings.AdditionalOverlays)
        {
            var camera = overlay.Camera;
            if (!_additionalOverlays.TryGetValue(camera.Slot, out var entry))
            {
                var tile = new CameraTile();
                tile.SharedDiagnostics = true;
                tile.DiagnosticsRequested += Tile_DiagnosticsRequested;
                tile.PointerActivity += Tile_PointerActivity;
                tile.FocusRequested += Tile_FocusRequested;
                var window = CreateOverlayWindow(camera.Name, tile);
                tile.Initialize(_libVlc, _logger, camera, _settings.RequestHardwareDecoding, compositedVideo: true);
                entry = (tile, window, camera);
            }
            else if (entry.Camera != camera) entry.Tile.Apply(camera);
            entry.Window.Title = $"RTSPView {camera.Name} Overlay";
            _additionalOverlays[camera.Slot] = (entry.Tile, entry.Window, camera);
        }
        _allTiles = [.. _tiles, DoorbellTile, GarageTile, .. _additionalOverlays.OrderBy(item => item.Key).Select(item => item.Value.Tile)];
    }
    private bool _overlayLayoutQueued;
    private AppSettings _settings = new();
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
    private int? _focusedSlot;
    private readonly SnapshotRefreshSchedule _snapshotSchedule = new();
    private bool _refreshingSnapshots;
    private UpdateBadgeWindow? _updateBadge;
    private TemperatureWarningWindow? _temperatureWarning;
    private TemperatureStatus? _temperatureStatus;
    private DateTime _lastTemperatureRead;
    private WallUpdateNotice? _updateNotice;
    private DateTime _lastUpdateNoticeRead;
    private bool _dialogOpen;

    public MainWindow()
    {
        _commandServer = new ViewerCommandServer(HandleCommandAsync);
        InitializeComponent();
        LocationChanged += (_, _) => QueueOverlayLayouts();
        SizeChanged += (_, _) => QueueOverlayLayouts();
        StateChanged += MainWindow_StateChanged;
        IsVisibleChanged += MainWindow_IsVisibleChanged;
        WallGrid.SizeChanged += (_, _) => QueueOverlayLayouts();
        WallViewport.SizeChanged += (_, _) => SizeWall();
        _cursorTimer.Tick += (_, _) =>
        {
            CheckCornerGesture();
            if (!_dialogOpen && _updateBadge?.Busy != true && _isFullScreen && _settings.HideMouseCursor &&
                DateTime.UtcNow - _lastMouseMovement >= TimeSpan.FromSeconds(_settings.MouseCursorHideSeconds))
            {
                if (Mouse.OverrideCursor is null) Mouse.OverrideCursor = System.Windows.Input.Cursors.None;
                foreach (var tile in _allTiles) tile.HideHoverControls();
            }
        };
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
        _settingsStore = new JsonSettingsStore(_settingsPath);
        _logger = new RollingFileLogger(Path.Combine(_dataDirectory, "logs"));
        _libVlc = new LibVLC("--no-video-title-show", "--no-osd", "--no-snapshot-preview");
        _libVlc.Log += (_, eventArgs) =>
        {
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
            RefreshAutomationOverlays();
            if (_settings.KeepViewerAlwaysOnTop) ApplyAlwaysOnTop();
            RefreshNativeVideoBackgrounds();
            RefreshUpdateBadge();
            RefreshTemperatureWarning();
            if (_settings.AllOverlays().Any(OverlayEnabled))
                QueueOverlayLayouts();
            if (DateTime.UtcNow - _lastLanAddressRefresh >= TimeSpan.FromSeconds(30)) UpdateLanAddressText();
            foreach (var tile in _allTiles) tile.Tick();
            DiagnosticsPanel.Refresh(_allTiles);
            DiagnosticsButton.Content = DiagnosticsPanel.WarningCount > 0 ? $"Diagnostics ({DiagnosticsPanel.WarningCount})" : "Diagnostics";
            RaiseWarningWindows();
            _telemetryPublisher.Publish(new ViewerTelemetry
            {
                ViewerUptimeSeconds = (long)_viewerUptime.Elapsed.TotalSeconds,
                ViewerMemoryMb = Math.Round(Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d, 1),
                HardwareDecoder = "See per-camera decoder status",
                Cameras = _allTiles.Select(tile => tile.GetTelemetry()).ToArray()
            });
            await ReloadExternalConfigurationAsync();
            if (!_refreshingSnapshots && _snapshotSchedule.IsDue(DateTimeOffset.UtcNow, _settings.Snapshots))
                _ = RefreshScheduledSnapshotsAsync();
        };
        _logger.Write("INFO", "Application startup: Phase 2 camera wall");
    }

    private async Task RefreshScheduledSnapshotsAsync()
    {
        _refreshingSnapshots = true;
        try
        {
            foreach (var tile in _allTiles.ToArray())
            {
                if (!_settings.Snapshots.Enabled) break;
                if (tile.IsPlaying) await tile.RefreshSnapshotAsync();
            }
        }
        catch (Exception exception) { _logger.Write("WARNING", $"Scheduled snapshot refresh failed: {exception.Message}"); }
        finally { _refreshingSnapshots = false; }
    }

    private void RefreshTemperatureWarning()
    {
        if (DateTime.UtcNow - _lastTemperatureRead > TimeSpan.FromSeconds(5))
        {
            _lastTemperatureRead = DateTime.UtcNow;
            try { _temperatureStatus = System.Text.Json.JsonSerializer.Deserialize<TemperatureStatus>(File.ReadAllText(Path.Combine(_dataDirectory, "temperature-status.json"))); }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { _temperatureStatus = null; }
        }
        var now = DateTimeOffset.UtcNow;
        if (!CanDisplayOverlayWindows() || (_temperatureStatus?.CpuWarning(now) != true && _temperatureStatus?.GpuWarning(now) != true))
        { _temperatureWarning?.SetStatus(null, now); return; }
        _temperatureWarning ??= new TemperatureWarningWindow(this);
        if (!_temperatureWarning.SetStatus(_temperatureStatus, now)) return;
        var origin = WallViewport.PointToScreen(new System.Windows.Point());
        var dpi = VisualTreeHelper.GetDpi(WallViewport);
        _temperatureWarning.PlaceWithin(new System.Windows.Point(origin.X / dpi.DpiScaleX, origin.Y / dpi.DpiScaleY),
            new System.Windows.Size(WallViewport.ActualWidth, WallViewport.ActualHeight));
        if (!_temperatureWarning.IsVisible) _temperatureWarning.Show();
        BringOverlayWindowToFront(_temperatureWarning);
    }

    private void RefreshUpdateBadge()
    {
        if (DateTime.UtcNow - _lastUpdateNoticeRead > TimeSpan.FromSeconds(5))
        {
            _lastUpdateNoticeRead=DateTime.UtcNow;
            try { _updateNotice=System.Text.Json.JsonSerializer.Deserialize<WallUpdateNotice>(File.ReadAllText(Path.Combine(_dataDirectory,"update-notice.json"))); }
            catch(Exception error) when(error is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { _updateNotice=null; }
        }
        if (_updateNotice?.Visible != true || !CanDisplayOverlayWindows()) { if(_updateBadge?.Busy!=true)_updateBadge?.Hide(); return; }
        if(_updateBadge is null)
        {
            _updateBadge=new UpdateBadgeWindow(this, showDialog: show => WithOverlaysSuppressed(show));
            _updateBadge.PointerActivity+=(_,_)=>RegisterPointerActivity();
        }
        _updateBadge.SetNotice(_updateNotice);
        var origin=WallViewport.PointToScreen(new System.Windows.Point());
        var dpi=VisualTreeHelper.GetDpi(WallViewport);
        _updateBadge.Left=origin.X/dpi.DpiScaleX+Math.Max(0,WallViewport.ActualWidth-_updateBadge.Width-16);
        _updateBadge.Top=origin.Y/dpi.DpiScaleY+Math.Max(0,WallViewport.ActualHeight-_updateBadge.Height-16);
        if(!_updateBadge.IsVisible && !_updateBadge.Busy)_updateBadge.Show();
        if(!_updateBadge.Busy)BringOverlayWindowToFront(_updateBadge);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureOverlayWindows();
        _tiles = AppSettings.MainCameraSlots.Select(_ => new CameraTile { Visibility = Visibility.Collapsed }).ToArray();
        foreach (var tile in _tiles) WallGrid.Children.Add(tile);
        SlotBox.ItemsSource = Enumerable.Range(1, _settings.CameraCount).ToArray();
        SlotBox.SelectedIndex = 0;
        _allTiles = [.. _tiles, DoorbellTile, GarageTile];
        foreach (var tile in _allTiles)
        {
            tile.SharedDiagnostics = true;
            tile.DiagnosticsRequested += Tile_DiagnosticsRequested;
            tile.PointerActivity += Tile_PointerActivity;
            tile.FocusRequested += Tile_FocusRequested;
        }
        _settings = (await _settingsStore.LoadAsync()).Normalize();
        SlotBox.ItemsSource = Enumerable.Range(1, _settings.CameraCount).ToArray();
        SlotBox.SelectedIndex = 0;
        _settingsLastWriteUtc = File.Exists(_settingsPath) ? File.GetLastWriteTimeUtc(_settingsPath) : DateTime.MinValue;
        var commandLineUrl = ReadArgument("--rtsp");
        var fillAll = Environment.GetCommandLineArgs().Any(value => value.Equals("--fill-all", StringComparison.OrdinalIgnoreCase));
        if (commandLineUrl is not null)
        {
            var cameras = _settings.Cameras.ToArray();
            var slots = fillAll ? Enumerable.Range(0, _tiles.Length) : [0];
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
        for (var index = 0; index < _tiles.Length; index++) _tiles[index].Initialize(_libVlc, _logger, _settings.Cameras[index], _settings.RequestHardwareDecoding);
        DoorbellTile.Initialize(_libVlc, _logger, _settings.DoorbellOverlay.Camera, _settings.RequestHardwareDecoding, compositedVideo: true);
        GarageTile.Initialize(_libVlc, _logger, _settings.GarageOverlay.Camera, _settings.RequestHardwareDecoding, compositedVideo: true);
        SyncAdditionalOverlays();
        ApplyWallLayout();
        ApplyOverlays();
        ApplyOverlayPreferences();
        LoadEditor(0);
        UpdateLanAddressText();
        PositionOnPreferredMonitor();
        SetFullScreen(_settings.StartFullScreen);
        _cursorTimer.Start();
        _diagnosticsTimer.Start();
    }

    private void ApplyWallLayout()
    {
        var layout = EffectiveLayout;
        if (_focusedSlot.HasValue && !_allTiles.Any(tile => tile.Slot == _focusedSlot)) _focusedSlot = null;
        foreach (var tile in _allTiles) tile.Focused = tile.Slot == EffectiveFocusedSlot;
        SizeWall();
        CameraWallPresentation.Apply(WallGrid, _tiles, layout, EffectiveFocusedSlot);
        QueueOverlayLayouts();
    }

    private void SizeWall()
    {
        var layout = EffectiveLayout;
        if (EffectiveFocusedSlot.HasValue)
        {
            WallGrid.Width = Math.Max(0, WallViewport.ActualWidth);
            WallGrid.Height = Math.Max(0, WallViewport.ActualHeight);
            return;
        }
        var size = layout.Fit(WallViewport.ActualWidth, WallViewport.ActualHeight);
        WallGrid.Width = size.Width;
        WallGrid.Height = size.Height;
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
        for (var slot = 1; slot <= AppSettings.MainCameraSlots.Length; slot++)
        {
            var name = $"--camera{slot}";
            for (var index = 1; index < arguments.Length - 1; index++)
                if (arguments[index].Equals(name, StringComparison.OrdinalIgnoreCase)) result[slot] = arguments[index + 1];
        }
        return result;
    }

    private void SlotBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && SlotBox.SelectedIndex >= 0) LoadEditor(Math.Max(0, SlotBox.SelectedIndex));
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
        DiagnosticsPanel.Refresh(_allTiles);
        DiagnosticsPanel.SetFullScreen(enabled);
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
        QueueOverlayLayouts();
        RefreshNativeVideoBackgrounds(forceRedraw: true);
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, async () =>
        {
            await Task.Delay(250);
            QueueOverlayLayouts();
            RefreshNativeVideoBackgrounds(forceRedraw: true);
        });
    }

    private void RefreshNativeVideoBackgrounds(bool forceRedraw = false)
    {
        NativeVideoBackgroundGuard.Apply(forceRedraw);
        foreach (var tile in _allTiles) tile.EnsureNativeVideoBackground(forceRedraw);
    }

    private void ApplyOverlays()
    {
        UpdateOverlayWindowLayouts();
        QueueOverlayLayouts();
    }

    private void EnsureOverlayWindows()
    {
        _doorbellWindow ??= CreateOverlayWindow("Doorbell", DoorbellTile);
        _garageWindow ??= CreateOverlayWindow("Garage", GarageTile);
    }

    private Window CreateOverlayWindow(string name, CameraTile tile)
    {
        var window = new Window
        {
            Owner = this,
            Title = $"RTSPView {name} Overlay",
            AllowsTransparency = true,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = System.Windows.Media.Brushes.Black,
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(0),
            Content = tile,
            Left = -32_000,
            Top = -32_000,
            Width = 1,
            Height = 1
        };
        window.SourceInitialized += (_, _) => ConfigureOverlayWindow(window);
        return window;
    }

    private void QueueOverlayLayouts()
    {
        if (!IsLoaded || _doorbellWindow is null || _garageWindow is null) return;
        if (!CanDisplayOverlayWindows())
        {
            HideOverlayWindows();
            return;
        }
        if (_overlayLayoutQueued) return;
        _overlayLayoutQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _overlayLayoutQueued = false;
            UpdateOverlayWindowLayouts();
        }));
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            _overlayLayoutQueued = false;
            HideOverlayWindows();
            return;
        }

        QueueOverlayLayouts();
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            _overlayLayoutQueued = false;
            HideOverlayWindows();
            return;
        }

        QueueOverlayLayouts();
    }

    private void HideOverlayWindows()
    {
        _temperatureWarning?.SetStatus(null, DateTimeOffset.UtcNow);
        _updateBadge?.Hide();
        HideOverlayWindowHierarchy(_doorbellWindow, DoorbellTile);
        HideOverlayWindowHierarchy(_garageWindow, GarageTile);
        foreach (var entry in _additionalOverlays.Values) HideOverlayWindowHierarchy(entry.Window, entry.Tile);
    }

    private static void HideOverlayWindowHierarchy(Window? overlayWindow, CameraTile overlayTile)
    {
        if (overlayWindow is null) return;
        foreach (Window ownedWindow in overlayWindow.OwnedWindows.Cast<Window>().ToArray())
            if (ownedWindow.IsVisible) ownedWindow.Hide();

        SetNativeWindowHierarchyVisibility(overlayTile.GetNativeVideoHandle(), false);
        var handle = new WindowInteropHelper(overlayWindow).Handle;
        if (handle != IntPtr.Zero) ShowWindow(handle, SwHide);
        if (overlayWindow.IsVisible) overlayWindow.Hide();
    }

    private bool CanDisplayOverlayWindows()
    {
        if (_dialogOpen || !IsVisible || WindowState == WindowState.Minimized) return false;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return true;
        if (!IsWindowVisible(handle) || IsIconic(handle)) return false;
        return DwmGetWindowAttribute(handle, DwmwaCloaked, out var cloaked, sizeof(uint)) != 0 || cloaked == 0;
    }

    private void UpdateOverlayWindowLayouts()
    {
        UpdateOverlayWindowLayout(_doorbellWindow, DoorbellTile, _settings.DoorbellOverlay);
        UpdateOverlayWindowLayout(_garageWindow, GarageTile, _settings.GarageOverlay);
        foreach (var overlay in _settings.AdditionalOverlays)
            if (_additionalOverlays.TryGetValue(overlay.Camera.Slot, out var entry))
                UpdateOverlayWindowLayout(entry.Window, entry.Tile, overlay);
        PlaceHostRestartButtons();
        RaiseWarningWindows();
    }

    private void UpdateOverlayWindowLayout(
        Window? overlayWindow,
        CameraTile overlayTile,
        DoorbellOverlaySettings overlay)
    {
        if (overlayWindow is null || _tiles.Length == 0) return;
        if (EffectiveFocusedSlot.HasValue)
        {
            if (EffectiveFocusedSlot != overlay.Camera.Slot || !CanDisplayOverlayWindows())
            {
                HideOverlayWindowHierarchy(overlayWindow, overlayTile);
                return;
            }
            var origin = WallGrid.PointToScreen(new System.Windows.Point());
            var scale = VisualTreeHelper.GetDpi(WallGrid);
            overlayWindow.Left = origin.X / scale.DpiScaleX;
            overlayWindow.Top = origin.Y / scale.DpiScaleY;
            overlayWindow.Width = Math.Max(1, WallGrid.ActualWidth);
            overlayWindow.Height = Math.Max(1, WallGrid.ActualHeight);
            var plain = overlay with { ViewportShape = DoorbellViewportShape.Native };
            overlayTile.ApplyVideoSizing(100, 50, 50, overlayWindow.Width, overlayWindow.Height);
            overlayTile.ApplyViewportEdgeSmoothing(plain, overlayWindow.Width, overlayWindow.Height);
            OverlayWindowOpacity.Apply(overlayWindow, 100);
            if (!overlayWindow.IsVisible) overlayWindow.Show();
            ShowOverlayWindowHierarchy(overlayWindow, overlayTile);
            SetWindowRgn(new WindowInteropHelper(overlayWindow).Handle, IntPtr.Zero, true);
            BringOverlayWindowToFront(overlayWindow);
            return;
        }
        if (!OverlayEnabled(overlay) || !CanDisplayOverlayWindows())
        {
            HideOverlayWindowHierarchy(overlayWindow, overlayTile);
            return;
        }

        var target = _tiles[Array.IndexOf(AppSettings.MainCameraSlots, overlay.HostCameraSlot)];
        if (!target.IsVisible || target.ActualWidth <= 0 || target.ActualHeight <= 0)
        {
            HideOverlayWindowHierarchy(overlayWindow, overlayTile);
            return;
        }
        System.Windows.Point screenOrigin;
        try { screenOrigin = target.PointToScreen(new System.Windows.Point(0, 0)); }
        catch (InvalidOperationException) { return; }
        var dpi = VisualTreeHelper.GetDpi(target);
        var targetLeft = screenOrigin.X / dpi.DpiScaleX;
        var targetTop = screenOrigin.Y / dpi.DpiScaleY;
        var bounds = CalculateOverlayBounds(target, overlay);
        var left = targetLeft + bounds.Left;
        var top = targetTop + bounds.Top;

        overlayWindow.Left = Math.Round(left);
        overlayWindow.Top = Math.Round(top);
        overlayWindow.Width = Math.Round(bounds.Width);
        overlayWindow.Height = Math.Round(bounds.Height);
        overlayTile.ApplyVideoSizing(
            overlay.ZoomPercent,
            overlay.ImageHorizontalPositionPercent,
            overlay.ImageVerticalPositionPercent,
            bounds.Width,
            bounds.Height);
        overlayTile.ApplyViewportEdgeSmoothing(overlay, bounds.Width, bounds.Height);
        OverlayWindowOpacity.Apply(overlayWindow, overlay.ViewportOpacityPercent);
        overlayWindow.Topmost = false;
        if (!overlayWindow.IsVisible) overlayWindow.Show();
        ShowOverlayWindowHierarchy(overlayWindow, overlayTile);
        ApplyOverlayWindowRegion(overlayWindow, overlay, bounds.Width, bounds.Height, dpi);
        BringOverlayWindowToFront(overlayWindow);
    }

    private static void ShowOverlayWindowHierarchy(Window overlayWindow, CameraTile overlayTile)
    {
        SetNativeWindowHierarchyVisibility(overlayTile.GetNativeVideoHandle(), true);
        foreach (Window ownedWindow in overlayWindow.OwnedWindows.Cast<Window>().ToArray())
        {
            ownedWindow.ShowActivated = false;
            ownedWindow.Opacity = overlayWindow.Opacity;
            if (!ownedWindow.IsVisible) ownedWindow.Show();
        }
    }

    private static void SetNativeWindowHierarchyVisibility(IntPtr root, bool visible)
    {
        if (root == IntPtr.Zero) return;
        var command = visible ? SwShowNoActivate : SwHide;
        ShowWindow(root, command);
        foreach (var descendant in EnumerateDescendantWindows(root)) ShowWindow(descendant, command);
    }

    private static Rect CalculateOverlayBounds(CameraTile target, DoorbellOverlaySettings overlay)
    {
        var bounds = OverlayGeometry.Calculate(overlay, target.ActualWidth, target.ActualHeight);
        return new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }

    private static void ApplyOverlayWindowRegion(
        Window overlayWindow,
        DoorbellOverlaySettings overlay,
        double width,
        double height,
        DpiScale dpi)
    {
        var shape = overlay.ViewportShape;
        var handle = new WindowInteropHelper(overlayWindow).Handle;
        if (handle == IntPtr.Zero) return;
        if (shape is DoorbellViewportShape.Native or DoorbellViewportShape.Square)
        {
            SetWindowRgn(handle, IntPtr.Zero, true);
            return;
        }

        var pixelWidth = Math.Max(1, (int)Math.Round(width * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Round(height * dpi.DpiScaleY));
        IntPtr region;
        if (shape == DoorbellViewportShape.Custom)
        {
            region = CustomViewportRegion.Create(overlay, pixelWidth, pixelHeight);
            if (region == IntPtr.Zero)
            {
                SetWindowRgn(handle, IntPtr.Zero, true);
                return;
            }
        }
        else if (shape is DoorbellViewportShape.Circle or DoorbellViewportShape.Oval)
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

    private void PlaceHostRestartButtons()
    {
        if (_tiles.Length == 0) return;
        var overlays = _settings.AllOverlays().ToArray();
        for (var index = 0; index < _tiles.Length; index++)
        {
            var target = _tiles[index];
            target.SetRestartButtonPlacement(RestartButtonPlacement.Center);
            var overlayBounds = overlays
                .Where(overlay => overlay.Camera.Enabled && overlay.HostCameraSlot == AppSettings.MainCameraSlots[index])
                .Select(overlay => CalculateOverlayBounds(target, overlay))
                .ToArray();
            target.SetRestartButtonCompact(overlayBounds.Length > 0);
            if (overlayBounds.Length > 0) PlaceHostRestartButton(target, overlayBounds);
        }
        DoorbellTile.SetRestartButtonPlacement(RestartButtonPlacement.Center);
        DoorbellTile.SetRestartButtonCompact(false);
        GarageTile.SetRestartButtonPlacement(RestartButtonPlacement.Center);
        GarageTile.SetRestartButtonCompact(false);
        foreach (var entry in _additionalOverlays.Values)
        {
            entry.Tile.SetRestartButtonPlacement(RestartButtonPlacement.Center);
            entry.Tile.SetRestartButtonCompact(false);
        }
    }

    private static void PlaceHostRestartButton(CameraTile target, IReadOnlyList<Rect> overlayBounds)
    {
        const double buttonWidth = 24;
        const double buttonHeight = 24;
        const double margin = 0;
        var tileWidth = target.ActualWidth;
        var tileHeight = target.ActualHeight;
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
        var overlayCenters = overlayBounds.Select(bounds => new System.Windows.Point(
            bounds.X + bounds.Width / 2,
            bounds.Y + bounds.Height / 2)).ToArray();
        var clearCandidates = candidates
            .Where(candidate => overlayBounds.All(bounds => !candidate.Bounds.IntersectsWith(bounds)))
            .ToArray();
        var best = (clearCandidates.Length > 0 ? clearCandidates : candidates)
            .OrderByDescending(candidate =>
                overlayCenters.Min(center =>
                    Math.Pow(candidate.Bounds.X + candidate.Bounds.Width / 2 - center.X, 2) +
                    Math.Pow(candidate.Bounds.Y + candidate.Bounds.Height / 2 - center.Y, 2)))
            .First();
        target.SetRestartButtonPlacement(best.Placement);

        (RestartButtonPlacement Placement, Rect Bounds) RestartCandidate(
            RestartButtonPlacement placement, double x, double y) =>
            (placement, new Rect(Math.Max(0, x), Math.Max(0, y),
                Math.Min(buttonWidth, tileWidth), Math.Min(buttonHeight, tileHeight)));
    }

    private static void ConfigureOverlayWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle,
            new IntPtr(extendedStyle | WsExToolWindow | WsExNoActivate));
        var borderColor = DwmColorNone;
        DwmSetWindowAttribute(handle, DwmwaBorderColor, ref borderColor, sizeof(uint));
        var cornerPreference = DwmWindowCornerPreferenceDoNotRound;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(uint));
    }

    private static void RefreshOverlayWindowStyle(IntPtr handle) =>
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoZOrder | SwpFrameChanged);

    private void BringOverlayWindowToFront(Window? overlayWindow)
    {
        RaiseWindow(overlayWindow);
        RaiseWarningWindows();
    }

    private void RaiseWarningWindows()
    {
        if (!CanDisplayOverlayWindows()) return;
        RaiseWindow(_updateBadge);
        RaiseWindow(_temperatureWarning);
    }

    private static void RaiseWindow(Window? overlayWindow)
    {
        if (overlayWindow?.IsVisible != true) return;
        var handle = new WindowInteropHelper(overlayWindow).Handle;
        if (handle == IntPtr.Zero) return;
        SetWindowPos(handle, HwndTop, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    private void ApplyAlwaysOnTop()
    {
        if (_dialogOpen) return;
        Topmost = _settings.KeepViewerAlwaysOnTop;
        if (_doorbellWindow is not null) _doorbellWindow.Topmost = false;
        if (_garageWindow is not null) _garageWindow.Topmost = false;
        foreach (var entry in _additionalOverlays.Values) entry.Window.Topmost = false;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        SetWindowPos(handle, _settings.KeepViewerAlwaysOnTop ? HwndTopmost : HwndNotTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
        if (CanDisplayOverlayWindows())
        {
            BringOverlayWindowToFront(_doorbellWindow);
            BringOverlayWindowToFront(_garageWindow);
            foreach (var entry in _additionalOverlays.Values) BringOverlayWindowToFront(entry.Window);
        }
        else
            HideOverlayWindows();
    }

    private static IReadOnlyList<IntPtr> EnumerateDescendantWindows(IntPtr root)
    {
        if (root == IntPtr.Zero) return [];
        var result = new List<IntPtr>();
        EnumChildWindows(root, (handle, _) =>
        {
            result.Add(handle);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaCloaked = 14;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const uint DwmWindowCornerPreferenceDoNotRound = 1;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr parameter);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref uint value, int valueSize);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out uint value, int valueSize);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) => RegisterPointerActivity();

    private void Tile_PointerActivity(object? sender, EventArgs e)
    {
        RegisterPointerActivity();
        if (sender is CameraTile tile) DiagnosticsPanel.SelectCamera(tile.Slot);
    }

    private void DiagnosticsButton_Click(object sender, RoutedEventArgs e) => DiagnosticsPanel.ToggleWindowed();

    private void Tile_DiagnosticsRequested(object? sender, EventArgs e)
    {
        RegisterPointerActivity();
        if (sender is CameraTile tile) DiagnosticsPanel.SelectCamera(tile.Slot, true);
    }

    private void Tile_FocusRequested(object? sender, EventArgs e)
    {
        if (sender is not CameraTile tile) return;
        // Reserve the fullscreen escape corner for its existing five-click gesture.
        if (_isFullScreen)
        {
            var position = System.Windows.Forms.Cursor.Position;
            var bounds = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds;
            if (position.X >= bounds.Right - 64 && position.Y < bounds.Top + 64) return;
        }
        if (_automatedSlots.Contains(tile.Slot) && _settings.AllOverlays().Any(o => o.Camera.Slot == tile.Slot && !o.Camera.Enabled))
        {
            DismissAutomation();
            RegisterPointerActivity();
            return;
        }
        _focusedSlot = EffectiveFocusedSlot == tile.Slot ? null : tile.Slot;
        DismissAutomation();
        RegisterPointerActivity();
        ApplyWallLayout();
    }

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
        if (WithOverlaysSuppressed(() => System.Windows.MessageBox.Show(this, "Leave RTSPView full-screen appliance mode?", "RTSPView", MessageBoxButton.YesNo, MessageBoxImage.Question)) == MessageBoxResult.Yes)
            SetFullScreen(false);
    }

    private Task<ViewerCommandResult> HandleCommandAsync(ViewerCommand command) =>
        Dispatcher.InvokeAsync(() => HandleCommandOnUiAsync(command)).Task.Unwrap();

    private async Task<ViewerCommandResult> HandleCommandOnUiAsync(ViewerCommand command)
    {
        var commandTile = _allTiles.FirstOrDefault(tile => tile.GetTelemetry().Slot == command.Slot);
        switch (command.Type)
        {
            case ViewerCommandType.AutomationOverlays:
                return ApplyAutomation(command);
            case ViewerCommandType.RestartCamera when commandTile is not null:
                commandTile.Start();
                var streamName = commandTile.GetTelemetry().Name;
                _logger.Write("INFO", $"Remote command: restarted {streamName}");
                return new ViewerCommandResult(command.Id, true, $"{streamName} restarted.");
            case ViewerCommandType.CaptureCameraSnapshot when commandTile is not null:
                var snapshotName = commandTile.GetTelemetry().Name;
                var captured = await commandTile.RefreshSnapshotAsync();
                _logger.Write(captured ? "INFO" : "WARNING", $"Remote command: {snapshotName} snapshot {(captured ? "refreshed" : "failed")}");
                return new ViewerCommandResult(command.Id, captured,
                    captured ? $"{snapshotName} snapshot refreshed." : $"{snapshotName} snapshot could not be captured.");
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
                _ = Dispatcher.BeginInvoke(System.Windows.Application.Current.Shutdown);
                return new ViewerCommandResult(command.Id, true, "Viewer restart started.");
            default:
                return new ViewerCommandResult(command.Id, false, "Invalid viewer command or camera slot.");
        }
    }

    private void ConfigureButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfigurationWindow(_settings, _settingsStore) { Owner = this };
        if (WithOverlaysSuppressed(() => dialog.ShowDialog()) != true) return;
        ClearAutomation();
        _settings = dialog.Settings.Normalize();
        SyncAdditionalOverlays();
        ApplyWallLayout();
        _settingsLastWriteUtc = File.GetLastWriteTimeUtc(_settingsPath);
        for (var index = 0; index < _tiles.Length; index++) _tiles[index].Apply(_settings.Cameras[index]);
        ApplyOverlays();
        DoorbellTile.Apply(_settings.DoorbellOverlay.Camera);
        GarageTile.Apply(_settings.GarageOverlay.Camera);
        ApplyOverlayPreferences();
        LoadEditor(Math.Max(0, SlotBox.SelectedIndex));
        _logger.Write("INFO", "Configuration saved and applied to all changed camera slots");
    }

    private void OpenWebConfig_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("http://127.0.0.1:5080") { UseShellExecute = true }); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            _logger.Write("ERROR", "Unable to open the web configuration browser: " + error.GetType().Name);
            WithOverlaysSuppressed(() => System.Windows.MessageBox.Show(this, "Open http://127.0.0.1:5080 in your browser. Make sure the RTSPView Controller is running.",
                "Web configuration", MessageBoxButton.OK, MessageBoxImage.Information));
        }
    }

    private T WithOverlaysSuppressed<T>(Func<T> show)
    {
        var previous = _dialogOpen;
        _dialogOpen = true;
        RegisterPointerActivity();
        foreach (var tile in _allTiles) tile.SetOverlaySuppressed(true);
        HideOverlayWindows();
        try { return show(); }
        finally
        {
            _dialogOpen = previous;
            foreach (var tile in _allTiles) tile.SetOverlaySuppressed(previous);
            if (!previous) { QueueOverlayLayouts(); RefreshUpdateBadge(); }
        }
    }

    private async Task ReloadExternalConfigurationAsync()
    {
        if (_dialogOpen || _reloadInProgress || !File.Exists(_settingsPath)) return;
        var writeTime = File.GetLastWriteTimeUtc(_settingsPath);
        if (writeTime <= _settingsLastWriteUtc) return;
        _reloadInProgress = true;
        try
        {
            var updated = (await _settingsStore.LoadAsync()).Normalize();
            ClearAutomation();
            var previousDoorbell = _settings.DoorbellOverlay.Camera;
            var previousGarage = _settings.GarageOverlay.Camera;
            for (var index = 0; index < _tiles.Length; index++)
                if (_settings.Cameras[index] != updated.Cameras[index]) _tiles[index].Apply(updated.Cameras[index]);
            var selectedCamera = Math.Max(0, SlotBox.SelectedIndex);
            _settings = updated;
            SlotBox.ItemsSource = Enumerable.Range(1, _settings.CameraCount).ToArray();
            SlotBox.SelectedIndex = Math.Min(selectedCamera, _settings.CameraCount - 1);
            SyncAdditionalOverlays();
            ApplyWallLayout();
            ApplyOverlays();
            if (previousDoorbell != updated.DoorbellOverlay.Camera)
                DoorbellTile.Apply(updated.DoorbellOverlay.Camera);
            if (previousGarage != updated.GarageOverlay.Camera)
                GarageTile.Apply(updated.GarageOverlay.Camera);
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
        _updateBadge?.Close();
        _temperatureWarning?.Close();
        Mouse.OverrideCursor = null;
        foreach (var tile in _allTiles) tile.Dispose();
        foreach (var entry in _additionalOverlays.Values)
        {
            entry.Window.Content = null;
            entry.Window.Close();
        }
        _additionalOverlays.Clear();
        if (_doorbellWindow is not null)
        {
            _doorbellWindow.Content = null;
            _doorbellWindow.Close();
            _doorbellWindow = null;
        }
        if (_garageWindow is not null)
        {
            _garageWindow.Content = null;
            _garageWindow.Close();
            _garageWindow = null;
        }
        _telemetryPublisher.Dispose();
        _commandServer.Dispose();
        _libVlc.Dispose();
        _logger.Write("INFO", "Application shutdown");
        base.OnClosing(e);
    }
}
