using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using RTSPView.Core;
using RTSPView.Infrastructure;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace RTSPView.Viewer;

public enum RestartButtonPlacement
{
    Center,
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

public partial class CameraTile : System.Windows.Controls.UserControl, IDisposable
{
    private MediaPlayer? _player;
    private Media? _media;
    private ResolvedStream? _streamLease;
    private CancellationTokenSource? _resolutionCancellation;
    private bool _resolving;

    private void CancelResolution()
    {
        _resolutionCancellation?.Cancel();
        _resolutionCancellation = null;
        _resolving = false;
        _streamLease?.Dispose();
        _streamLease = null;
    }
    private LibVLC? _libVlc;
    private RollingFileLogger? _logger;
    private CameraSettings _settings = new();
    private CameraRuntimeStatus _status = new();
    private DateTimeOffset _attemptStartedAt;
    private DateTimeOffset? _healthySince;
    private StreamActivity _activity = new();
    private volatile string _decoder = "Unknown";
    private bool _ownsEngine;
    private long _snapshotCapturedTicks;
    private long _lastLostPictures = -1;
    private long _pendingLostPictures;
    private DateTimeOffset _lastFrameLossLogAt = DateTimeOffset.MinValue;
    private bool _requestHardwareDecoding;
    private bool _useCompositedOutput;
    private CompositedVideoPresenter? _compositedPresenter;
    private bool _disposed;
    private bool _showCameraNames = true;
    private bool _showCameraStats = true;
    private readonly object _operationGate = new();
    private Task _playerOperation = Task.CompletedTask;
    private int _playGeneration;
    private nint _nativeVideoHandle;
    private HwndSource? _backgroundSource;
    private bool _pendingNativeStart;
    private bool _pendingNativeRecreate;
    private int _videoZoomPercent = 100;
    private int _videoDisplayWidth;
    private int _videoDisplayHeight;
    private int _imageHorizontalPositionPercent = 50;
    private int _imageVerticalPositionPercent = 50;
    private uint _lastSizingSourceWidth;
    private uint _lastSizingSourceHeight;
    private bool _nativeVideoLayoutConfirmed;
    private readonly string _snapshotDirectory = Path.Combine(AppPaths.DataDirectory, "snapshots");

    public int Slot => _settings.Slot;
    public bool OwnsDecoder => _sharedSource is null;
    public bool IsPlaying => _sharedSource?.IsPlaying ?? (_player?.IsPlaying == true);
    public CameraRuntimeStatus Status => _sharedSource?.Status ?? _status;
    public event EventHandler? PointerActivity;
    public event EventHandler? FocusRequested;
    public event EventHandler? DiagnosticsRequested;
    public bool SharedDiagnostics { get; set; }
    public bool DiagnosticsAvailable => _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.RtspUrl);
    public string DiagnosticLabel => $"{_settings.Name} ({(_useCompositedOutput ? "Overlay" : "Main feed")} · {Slot})";
    public string? DiagnosticWarning
    {
        get
        {
            if (!DiagnosticsAvailable) return null;
            if (_sharedSource is not null) return _sharedSource.DiagnosticWarning;
            var frame = FrameWarning(DateTimeOffset.UtcNow);
            if (_status.State is CameraConnectionState.Live or CameraConnectionState.Disabled or CameraConnectionState.NotConfigured) return frame;
            return StateLabel(_status.State) +
                (string.IsNullOrWhiteSpace(_status.LastError) ? "" : " — " + RollingFileLogger.RedactCredentials(_status.LastError)) +
                (frame is null ? "" : "\n" + frame);
        }
    }
    public bool Focused { get; set; }

    private void DiagnosticBadge_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        DiagnosticsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OverlayRoot_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        // Button clicks must never also change the wall layout.
        if (e.OriginalSource is DependencyObject source)
            for (var node = source; node is not null; node = node is FrameworkContentElement content ? content.Parent : System.Windows.Media.VisualTreeHelper.GetParent(node))
                if (node is System.Windows.Controls.Primitives.ButtonBase) return;
        FocusRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    public CameraTelemetry GetTelemetry()
    {
        if (_sharedSource is not null) return _sharedSource.GetTelemetry() with {
            Slot = Slot, Name = _settings.Name, Visible = IsVisible, ConfiguredPlayer = false, SharedDecoderSlot = _sharedSource.Slot, CompositedUploads = 0,
            SnapshotCapturedAt = Interlocked.Read(ref _snapshotCapturedTicks) is var captured && captured > 0 ? new DateTimeOffset(captured, TimeSpan.Zero) : null };
        var stats = _media?.Statistics;
        var videoTrack = _media?.Tracks.FirstOrDefault(track => track.TrackType == TrackType.Video);
        return new CameraTelemetry
        {
            Slot = _settings.Slot,
            Name = _settings.Name,
            State = _status.State.ToString(),
            Fps = IsPlaying && FrameWarning(DateTimeOffset.UtcNow) is null ? (float)_activity.FramesPerSecond : 0,
            BitrateKbps = !IsPlaying || stats is null ? 0 : Math.Round(stats.Value.DemuxBitrate * 8000, 1),
            Codec = videoTrack is null ? null : FourCc(videoTrack.Value.Codec),
            Width = videoTrack?.Data.Video.Width,
            Height = videoTrack?.Data.Video.Height,
            Decoder = _decoder,
            ConfiguredPlayer = DiagnosticsAvailable,
            Visible = IsVisible,
            CompositedUploads = _compositedPresenter?.UploadCount ?? 0,
            SnapshotCapturedAt = Interlocked.Read(ref _snapshotCapturedTicks) is var ticks && ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null,
            FrameWarning = FrameWarning(DateTimeOffset.UtcNow),
            ReconnectCount = _status.ReconnectCount,
            StreamUptimeSeconds = _status.ConnectedAt is null ? null : (long)(DateTimeOffset.UtcNow - _status.ConnectedAt.Value).TotalSeconds,
            StreamStartedAt = _status.ConnectedAt,
            LastFrameAt = _status.LastFrameAt,
            LastReconnectAt = _status.LastReconnectAt,
            LastError = _status.LastError
        };
    }

    private static string FourCc(uint value) => new string([(char)(value & 0xff), (char)((value >> 8) & 0xff), (char)((value >> 16) & 0xff), (char)((value >> 24) & 0xff)]).Trim('\0').ToUpperInvariant();

    public System.Windows.Controls.Grid ContentOverlayRoot => OverlayRoot;

    public CameraTile()
    {
        InitializeComponent();
        VideoView.Loaded += (_, _) => { EnsureNativeVideoBackground(); ResumeNativeStart(); ApplyVideoSizing(_player); };
        VideoView.SizeChanged += (_, _) => ApplyVideoSizing(_player);
        CompositedCanvas.SizeChanged += (_, _) => { if (_preserveWholeFrame) ApplyVideoSizing(_player); };
        IsVisibleChanged += (_, _) => { SynchronizeStatusWindowVisibility(); ResumeNativeStart(); };
        Loaded += (_, _) => SynchronizeStatusWindowVisibility();
        // VideoView moves OverlayRoot into a separate top-level window. Parent
        // visibility does not inherit across that boundary, including on first load.
        OverlayRoot.Loaded += (_, _) => SynchronizeStatusWindowVisibility();
    }

    public void SetWallVisibility(bool visible)
    {
        if (!visible) OverlayRoot.Visibility = Visibility.Collapsed;
        // Hidden keeps the native video host measured and attached to the wall,
        // without showing video or diagnostics. Collapsed cannot create an HWND
        // for a camera that has never been assigned to a layout.
        Visibility = visible ? Visibility.Visible : (_useCompositedOutput ? Visibility.Collapsed : Visibility.Hidden);
        SynchronizeStatusWindowVisibility();
    }

    private bool _overlaySuppressed;
    public void SetOverlaySuppressed(bool suppressed)
    {
        _overlaySuppressed = suppressed;
        SynchronizeStatusWindowVisibility();
    }

    private void SynchronizeStatusWindowVisibility()
    {
        OverlayRoot.Visibility = IsVisible && !_overlaySuppressed ? Visibility.Visible : Visibility.Collapsed;
        if (_useCompositedOutput) return;
        var statusWindow = Window.GetWindow(OverlayRoot);
        // Composited overlays use their actual owner, which MainWindow manages.
        if (statusWindow is null || statusWindow == Window.GetWindow(this)) return;
        if (!IsVisible || _overlaySuppressed)
        {
            if (statusWindow.IsVisible) statusWindow.Hide();
            HideHoverControls();
        }
        else if (!statusWindow.IsVisible && ActualWidth > 0 && ActualHeight > 0)
        {
            statusWindow.Show();
        }
    }

    private bool _preserveWholeFrame;

    public void Initialize(LibVLC libVlc, RollingFileLogger logger, CameraSettings settings, bool requestHardwareDecoding, bool compositedVideo = false, bool preserveWholeFrame = false, CameraTile? sharedSource = null)
    {
        _sharedSource = sharedSource;
        _preserveWholeFrame = preserveWholeFrame;
        _useCompositedOutput = compositedVideo;
        settings = PlaybackSettings(settings);
        if (compositedVideo)
        {
            VideoView.Content = null;
            VideoView.Visibility = Visibility.Collapsed;
            CompositedCanvas.Visibility = Visibility.Visible;
            RenderRoot.Children.Add(OverlayRoot);
        }
        // LibVLC logs have no reliable media-player identity. A camera-owned engine
        // lets decoder evidence describe this stream rather than another camera.
        _libVlc = compositedVideo ? libVlc : new LibVLC("--no-video-title-show", "--no-osd", "--no-snapshot-preview");
        _ownsEngine = !compositedVideo;
        if (_ownsEngine) _libVlc.Log += OnDecoderLog;
        _logger = logger;
        _settings = settings;
        _requestHardwareDecoding = requestHardwareDecoding;
        _decoder = compositedVideo || !requestHardwareDecoding ? "Software decoding" : "Hardware requested (unconfirmed)";
        _attemptStartedAt = DateTimeOffset.UtcNow;
        OverlayRoot.ToolTip = "Double-click the video to focus; double-click again to restore the wall.";
        Directory.CreateDirectory(_snapshotDirectory);
        NameText.Text = settings.Name;
        SlotText.Text = $"Slot {settings.Slot}";
        UpdateRestartButton();
        if (_sharedSource is not null)
        {
            if (!_sharedSource._useCompositedOutput) throw new InvalidOperationException("Shared frames require a composited source.");
            _sharedSource._frameMirrors.Add(this);
            _sharedSource._compositedPresenter?.AddMirror(CompositedImage, () => ApplyVideoSizing(null));
            return;
        }
        CreatePlayer();
        if (settings.Enabled && !string.IsNullOrWhiteSpace(settings.RtspUrl)) Start(manual: true);
        else SetState(settings.Enabled ? CameraConnectionState.NotConfigured : CameraConnectionState.Disabled);
    }

    public void SetHardwareDecoding(bool requested)
    {
        if (_requestHardwareDecoding == requested) return;
        _requestHardwareDecoding = requested;
        if (!_useCompositedOutput && _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.RtspUrl))
            StartPlayer(recreatePlayer: true);
    }

    public void Apply(CameraSettings settings, bool force = false)
    {
        settings = PlaybackSettings(settings);
        if (_sharedSource is not null) { _settings = settings; NameText.Text = settings.Name; return; }
        // Overlay display-mode changes only affect MainWindow's visibility gate.
        // Keep the existing player, decoded frame and recovery state intact.
        if (!force && _useCompositedOutput && _settings == settings) return;
        if (_settings.RtspUrl != settings.RtspUrl)
        {
            _activity = new StreamActivity();
            _status = _status with { LastFrameAt = null };
        }
        _settings = settings;
        NameText.Text = settings.Name;
        SlotText.Text = $"Slot {settings.Slot}";
        UpdateRestartButton();
        if (settings.Enabled && !string.IsNullOrWhiteSpace(settings.RtspUrl)) Start(manual: true);
        else Stop(settings.Enabled ? CameraConnectionState.NotConfigured : CameraConnectionState.Disabled);
    }

    private CameraSettings PlaybackSettings(CameraSettings settings) =>
        _useCompositedOutput && !string.IsNullOrWhiteSpace(settings.RtspUrl)
            ? settings with { Enabled = true }
            : settings;

    public void ApplyTileBorder(bool visible) => TileBorder.BorderThickness = new Thickness(visible ? 1 : 0);

    private uint _wallBackgroundColor;
    public void ApplyWallAppearance(WallLayout layout, bool defaultBorders = true)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(layout.BackgroundColor);
        var brush = new System.Windows.Media.SolidColorBrush(color);
        Background = TileBorder.Background = VideoView.Background = CompositedCanvas.Background = brush;
        TileBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(layout.BorderColor));
        ApplyTileBorder(layout.ShowTileBorders ?? defaultBorders);
        var nativeColor = (uint)(color.R | color.G << 8 | color.B << 16);
        var changed = _wallBackgroundColor != nativeColor;
        _wallBackgroundColor = nativeColor;
        EnsureNativeVideoBackground(changed);
    }

    public void ApplyOverlayPreferences(bool showCameraNames, bool showCameraStats)
    {
        _showCameraNames = showCameraNames;
        _showCameraStats = showCameraStats;
        UpdateOverlayPresentation(DateTimeOffset.UtcNow);
    }

    public (uint Width, uint Height)? GetVideoDimensions()
    {
        if (_disposed) return null;
        if (_sharedSource is not null) return _sharedSource.GetVideoDimensions();
        var videoTrack = _media?.Tracks.FirstOrDefault(track => track.TrackType == TrackType.Video);
        if (videoTrack is null || videoTrack.Value.Data.Video.Width == 0 || videoTrack.Value.Data.Video.Height == 0)
            return null;
        return (videoTrack.Value.Data.Video.Width, videoTrack.Value.Data.Video.Height);
    }

    private double _overlayReferenceWidth, _overlayReferenceHeight;

    public DoorbellVideoLayout GetHostImageLayout()
    {
        var source = GetVideoDimensions() ?? (1600u, 900u);
        var surface = _useCompositedOutput ? (FrameworkElement)CompositedCanvas : VideoView;
        var origin = surface.TranslatePoint(new System.Windows.Point(), this);
        var image = WallVideoTransform.Calculate(source.Item1, source.Item2,
            Math.Max(1, surface.ActualWidth), Math.Max(1, surface.ActualHeight), _wallSizing ?? "fit",
            _outputTileWidth, _videoZoomPercent, _imageHorizontalPositionPercent, _imageVerticalPositionPercent);
        return image with { OffsetX = image.OffsetX + origin.X, OffsetY = image.OffsetY + origin.Y };
    }

    public void ApplyVideoSizing(
        int zoomPercent,
        int imageHorizontalPositionPercent,
        int imageVerticalPositionPercent,
        double width,
        double height, double referenceWidth = 0, double referenceHeight = 0)
    {
        var displayWidth = Math.Max(1, (int)Math.Round(width));
        var displayHeight = Math.Max(1, (int)Math.Round(height));
        zoomPercent = Math.Clamp(zoomPercent, 100, 300);
        imageHorizontalPositionPercent = Math.Clamp(imageHorizontalPositionPercent, 0, 100);
        imageVerticalPositionPercent = Math.Clamp(imageVerticalPositionPercent, 0, 100);
        if (_videoZoomPercent == zoomPercent &&
            _imageHorizontalPositionPercent == imageHorizontalPositionPercent &&
            _imageVerticalPositionPercent == imageVerticalPositionPercent &&
            _videoDisplayWidth == displayWidth && _videoDisplayHeight == displayHeight &&
            _overlayReferenceWidth == referenceWidth && _overlayReferenceHeight == referenceHeight) return;
        _overlayReferenceWidth = referenceWidth;
        _overlayReferenceHeight = referenceHeight;
        _videoZoomPercent = zoomPercent;
        _imageHorizontalPositionPercent = imageHorizontalPositionPercent;
        _imageVerticalPositionPercent = imageVerticalPositionPercent;
        _videoDisplayWidth = displayWidth;
        _videoDisplayHeight = displayHeight;
        ApplyVideoSizing(_player);
    }

    public void ApplyViewportEdgeSmoothing(DoorbellOverlaySettings overlay, double width, double height,
        double referenceWidth = 0, double referenceHeight = 0)
    {
        TileBorder.BorderThickness = new Thickness(0);
        EllipticalViewportEdge.Visibility = RoundedViewportEdge.Visibility = CustomViewportEdge.Visibility = Visibility.Collapsed;
        var geometry = referenceWidth > 0 && referenceHeight > 0
            ? OverlayViewportGeometry.Create(overlay, referenceWidth, referenceHeight).Clone()
            : OverlayViewportGeometry.Create(overlay, width, height);
        if (referenceWidth > 0 && referenceHeight > 0)
        {
            var transform = new System.Windows.Media.TransformGroup();
            transform.Children.Add(geometry.Transform);
            transform.Children.Add(new System.Windows.Media.ScaleTransform(width / referenceWidth, height / referenceHeight));
            geometry.Transform = transform;
        }
        var drawing = new System.Windows.Media.DrawingGroup();
        drawing.Children.Add(new System.Windows.Media.GeometryDrawing(System.Windows.Media.Brushes.Transparent, null,
            new System.Windows.Media.RectangleGeometry(new Rect(0, 0, width, height))));
        drawing.Children.Add(new System.Windows.Media.GeometryDrawing(System.Windows.Media.Brushes.White, null, geometry));
        OpacityMask = new System.Windows.Media.DrawingBrush(drawing)
        {
            ViewboxUnits = System.Windows.Media.BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, width, height),
            Stretch = System.Windows.Media.Stretch.Fill
        };
        ViewportBorder.Data = geometry;
        ViewportBorder.Visibility = overlay.ShowBorder ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetRestartButtonPlacement(RestartButtonPlacement placement)
    {
        RestartStreamButton.HorizontalAlignment = placement switch
        {
            RestartButtonPlacement.TopLeft or RestartButtonPlacement.CenterLeft or RestartButtonPlacement.BottomLeft => System.Windows.HorizontalAlignment.Left,
            RestartButtonPlacement.TopRight or RestartButtonPlacement.CenterRight or RestartButtonPlacement.BottomRight => System.Windows.HorizontalAlignment.Right,
            _ => System.Windows.HorizontalAlignment.Center
        };
        RestartStreamButton.VerticalAlignment = placement switch
        {
            RestartButtonPlacement.TopLeft or RestartButtonPlacement.TopCenter or RestartButtonPlacement.TopRight => System.Windows.VerticalAlignment.Top,
            RestartButtonPlacement.BottomLeft or RestartButtonPlacement.BottomCenter or RestartButtonPlacement.BottomRight => System.Windows.VerticalAlignment.Bottom,
            _ => System.Windows.VerticalAlignment.Center
        };
    }

    public void SetRestartButtonCompact(bool compact)
    {
        RestartStreamButton.Width = compact ? 24 : 118;
        RestartStreamButton.Height = compact ? 24 : 32;
        RestartStreamButton.Margin = compact ? new Thickness(0) : new Thickness(2);
        RestartStreamButton.Content = compact ? "↻" : "Restart stream";
        RestartStreamButton.FontSize = compact ? 17 : 12;
    }

    private bool _pluginEnabled = true;
    public void SetPluginEnabled(bool enabled)
    {
        if (_pluginEnabled == enabled) return;
        _pluginEnabled = enabled;
        if (!enabled) Stop(CameraConnectionState.Disabled);
        else if (_settings.Enabled && !string.IsNullOrWhiteSpace(_settings.RtspUrl)) Start();
    }
    public void Start(bool manual = true)
    {
        if (!_pluginEnabled) { Stop(CameraConnectionState.Disabled); return; }
        if (_sharedSource is not null) { _sharedSource.Start(manual); return; }
        if (_disposed || _libVlc is null || !_settings.Enabled || string.IsNullOrWhiteSpace(_settings.RtspUrl)) return;
        if (manual)
            _status = _status with { ConsecutiveFailures = 0, NextReconnectAt = null, LastError = null };
        StartPlayer(recreatePlayer: false);
    }

    private void RestartStreamButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _logger?.Write("INFO", $"Camera {_settings.Slot} ({_settings.Name}): local tile restart requested");
        Start();
    }

    private void OverlayRoot_MouseEnter(object sender, MouseEventArgs e)
    {
        RestartStreamButton.Visibility = SharedDiagnostics ? Visibility.Collapsed : Visibility.Visible;
        PointerActivity?.Invoke(this, EventArgs.Empty);
    }

    private void OverlayRoot_MouseMove(object sender, MouseEventArgs e) =>
        PointerActivity?.Invoke(this, EventArgs.Empty);

    private void OverlayRoot_MouseLeave(object sender, MouseEventArgs e) =>
        RestartStreamButton.Visibility = Visibility.Collapsed;

    public void HideHoverControls() => RestartStreamButton.Visibility = Visibility.Collapsed;

    public void EnsureNativeVideoBackground(bool forceRedraw = false)
    {
        if (_useCompositedOutput) return;
        VideoView.ApplyTemplate();
        if (VideoView.Template.FindName("PART_PlayerHost", VideoView) is not HwndHost videoHost ||
            videoHost.Handle == IntPtr.Zero)
            return;

        var handle = (nint)videoHost.Handle;
        var parent = HwndSource.FromHwnd(NativeVideoBackgroundGuard.GetHostParent(handle));
        var parentChanged = _backgroundSource != parent;
        if (parentChanged)
        {
            _backgroundSource?.RemoveHook(ColorNativeVideoBackground);
            _backgroundSource = parent;
            _backgroundSource?.AddHook(ColorNativeVideoBackground);
        }
        var handleChanged = handle != _nativeVideoHandle;
        _nativeVideoHandle = handle;
        NativeVideoBackgroundGuard.Apply(handle, forceRedraw || handleChanged || parentChanged);
    }

    private nint ColorNativeVideoBackground(nint handle, int message, nint wParam, nint lParam, ref bool handled) =>
        NativeVideoBackgroundGuard.ColorHostBackground(_nativeVideoHandle, message, wParam, lParam, ref handled, _wallBackgroundColor);

    private void ResumeNativeStart()
    {
        if (!_pendingNativeStart || _disposed || !VideoView.IsLoaded) return;
        var recreate = _pendingNativeRecreate;
        StartPlayer(recreate);
    }

    public IntPtr GetNativeVideoHandle()
    {
        if (_useCompositedOutput) return IntPtr.Zero;
        VideoView.ApplyTemplate();
        if (VideoView.Template.FindName("PART_PlayerHost", VideoView) is not HwndHost videoHost ||
            videoHost.Handle == IntPtr.Zero)
            return IntPtr.Zero;

        _nativeVideoHandle = videoHost.Handle;
        return videoHost.Handle;
    }

    private void UpdateRestartButton() => RestartStreamButton.IsEnabled =
        _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.RtspUrl);

    private string? FrameWarning(DateTimeOffset now) => _sharedSource is not null ? _sharedSource.FrameWarning(now) : !_settings.Enabled || string.IsNullOrWhiteSpace(_settings.RtspUrl) || _pendingNativeStart || _resolving
        ? null : _activity.Warning(now, _attemptStartedAt);

    private void OnDecoderLog(object? sender, LogEventArgs e)
    {
        var message = e.FormattedLog;
        if (!_useCompositedOutput && _requestHardwareDecoding &&
            (message.Contains("for hardware decoding", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("using hw decoder module", StringComparison.OrdinalIgnoreCase)))
        {
            var module = System.Text.RegularExpressions.Regex.Match(message,
                @"\b(d3d11va|dxva2|nvdec|cuda|vaapi|vdpau|videotoolbox)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            _decoder = module.Success ? $"Hardware active ({module.Value.ToUpperInvariant()})" : "Hardware active";
        }
        if (e.Level >= LogLevel.Warning && !message.Contains("picture is too late to be displayed", StringComparison.OrdinalIgnoreCase))
            _logger?.Write($"VLC-{e.Level}", $"Camera {_settings.Slot}: {message}");
    }

    public void Tick()
    {
        if (_sharedSource is not null) { _status = _sharedSource.Status; ApplyVideoSizing(null); UpdateOverlayPresentation(DateTimeOffset.UtcNow); return; }
        if (_pendingNativeStart) { ResumeNativeStart(); return; }
        if (_disposed || !_settings.Enabled || _resolving) return;
        if (_videoDisplayWidth > 0 && _videoDisplayHeight > 0)
        {
            var dimensions = GetVideoDimensions();
            if (dimensions is not null)
                ApplyVideoSizing(_player);
        }
        var now = DateTimeOffset.UtcNow;
        UpdateOverlayPresentation(now);

        if (_status.NextReconnectAt is not null)
        {
            var remaining = _status.NextReconnectAt.Value - now;
            if (remaining <= TimeSpan.Zero)
            {
                var recreate = _status.ConsecutiveFailures >= 3;
                StartPlayer(recreate);
            }
            else
                SetOverlay($"Reconnecting in {Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))}s", true);
            return;
        }

        if (!_resolving && _status.State is CameraConnectionState.Connecting or CameraConnectionState.Buffering &&
            now - _attemptStartedAt > TimeSpan.FromSeconds(Math.Clamp(_settings.StartupTimeoutSeconds, 8, 120)))
        {
            ScheduleRecovery("Stream startup timed out");
            return;
        }

        if (_player?.IsPlaying == true)
        {
            ObserveFrameProgress(now);
            if (now - (_status.LastFrameAt > _attemptStartedAt ? _status.LastFrameAt.Value : _attemptStartedAt) > TimeSpan.FromSeconds(Math.Clamp(_settings.WatchdogTimeoutSeconds, 8, 120)))
            {
                ScheduleRecovery("No decoded-frame progress; watchdog detected a stalled stream");
                return;
            }

            if (_healthySince is not null && now - _healthySince > TimeSpan.FromSeconds(60) && _status.ConsecutiveFailures > 0)
                _status = _status with { ConsecutiveFailures = 0 };

            UpdateOverlayPresentation(now);
        }
        else UpdateOverlayPresentation(now);
    }

    private void UpdateOverlayPresentation(DateTimeOffset now)
    {
        var warning = FrameWarning(now);
        StaleBanner.Visibility = warning is null ? Visibility.Collapsed : Visibility.Visible;
        StaleText.Text = warning is null ? string.Empty : "STALE VIDEO — " + warning;
        var connectionNeedsAttention = _settings.Enabled && _status.State is not CameraConnectionState.Live and not CameraConnectionState.Disabled and not CameraConnectionState.NotConfigured;
        // Background overlays can be revealed during this recovery window. A
        // healthy feed should honor the user's display preferences on reveal.
        var recentlyRecovered = !_useCompositedOutput && _status.State == CameraConnectionState.Live && _healthySince is not null && now - _healthySince < TimeSpan.FromSeconds(15);
        FocusText.Visibility = Focused ? Visibility.Visible : Visibility.Collapsed;
        var forceVisible = connectionNeedsAttention || recentlyRecovered || Focused;
        var showName = _showCameraNames || forceVisible;
        var showStats = _showCameraStats || forceVisible;

        NameText.Visibility = showName ? Visibility.Visible : Visibility.Collapsed;
        if (showStats)
        {
            if (_status.State == CameraConnectionState.Live && _player is not null)
            {
                var telemetry = GetTelemetry();
                var resolution = telemetry.Width > 0 && telemetry.Height > 0 ? $"{telemetry.Width}×{telemetry.Height}" : "Resolution unknown";
                var bitrate = $" • {telemetry.BitrateKbps:0} kb/s";
                var uptime = _status.ConnectedAt is null ? string.Empty : $" • {(now - _status.ConnectedAt.Value):hh\\:mm\\:ss}";
                DetailsText.Text = $"{resolution} • {telemetry.Codec ?? "Codec unknown"} • {telemetry.Fps:0.0} fps{bitrate} • {_decoder} • R:{_status.ReconnectCount}{uptime}";
            }
            else DetailsText.Text = $"{_status.State} • R:{_status.ReconnectCount}";
            DetailsText.Visibility = Visibility.Visible;
        }
        else
        {
            DetailsText.Text = string.Empty;
            DetailsText.Visibility = Visibility.Collapsed;
        }
        OverlayPanel.Visibility = showName || showStats ? Visibility.Visible : Visibility.Collapsed;
        if (SharedDiagnostics)
        {
            // Only the compact badge remains inside the clipped video surface.
            DetailsText.Visibility = FocusText.Visibility = Visibility.Collapsed;
            NameText.Visibility = _showCameraNames ? Visibility.Visible : Visibility.Collapsed;
            OverlayPanel.Visibility = _showCameraNames && !_useCompositedOutput ? Visibility.Visible : Visibility.Collapsed;
            StateText.Visibility = SlotText.Visibility = StaleBanner.Visibility = Visibility.Collapsed;
            DiagnosticBadge.Visibility = DiagnosticWarning is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void ObserveFrameProgress(DateTimeOffset now)
    {
        var stats = _media?.Statistics;
        if (stats is null) return;
        _activity.Observe(stats.Value.DecodedVideo, now);
        _status = _status.WithFrameProgress(_activity.LastFrameAt);

        var lostPictures = (long)stats.Value.LostPictures;
        if (_lastLostPictures >= 0)
        {
            var newlyLost = lostPictures >= _lastLostPictures ? lostPictures - _lastLostPictures : lostPictures;
            _pendingLostPictures += newlyLost;
        }
        _lastLostPictures = lostPictures;

        if (_pendingLostPictures > 0 && now - _lastFrameLossLogAt >= TimeSpan.FromSeconds(5))
        {
            _logger?.Write("VLC-Warning", $"Camera {_settings.Slot} ({_settings.Name}): {_pendingLostPictures} video frame(s) lost; displayed={stats.Value.DisplayedPictures}, lost-total={lostPictures}");
            _pendingLostPictures = 0;
            _lastFrameLossLogAt = now;
        }
    }

    private void CreatePlayer()
    {
        if (_libVlc is null) return;
        var player = new MediaPlayer(_libVlc) { EnableHardwareDecoding = _requestHardwareDecoding && !_useCompositedOutput };
        _player = player;
        if (_useCompositedOutput)
        {
            _compositedPresenter = new CompositedVideoPresenter(player, CompositedImage, () => ApplyVideoSizing(player));
            foreach (var mirror in _frameMirrors) _compositedPresenter.AddMirror(mirror.CompositedImage, () => mirror.ApplyVideoSizing(null));
        }
        else VideoView.MediaPlayer = player;
        _lastSizingSourceWidth = 0;
        _lastSizingSourceHeight = 0;
        _nativeVideoLayoutConfirmed = false;
        ApplyVideoSizing(player);
        player.Opening += (_, _) => { if (!_disposed && ReferenceEquals(_player, player)) SetState(CameraConnectionState.Connecting); };
        player.Buffering += (_, e) =>
        {
            if (_disposed || !ReferenceEquals(_player, player)) return;
            if (e.Cache >= 99.5f && player.IsPlaying) SetState(CameraConnectionState.Live);
            else SetState(CameraConnectionState.Buffering, $"Buffering {e.Cache:0}%");
        };
        player.Playing += (_, _) =>
        {
            if (_disposed || !ReferenceEquals(_player, player)) return;
            var now = DateTimeOffset.UtcNow;
            _healthySince = now;
            _status = _status with { State = CameraConnectionState.Live, ConnectedAt = now, NextReconnectAt = null };
            var generation = _playGeneration;
            _ = CaptureSnapshotAsync(player, generation, waitForFirstFrame: true);
            SetOverlay("Live", false);
            Dispatcher.BeginInvoke(() => ApplyVideoSizing(player));
            var effectiveOptions = string.Join(' ', _settings.ToMediaOptions().Where(option => option.StartsWith(":rtsp-", StringComparison.Ordinal) || option.StartsWith(":network-caching=", StringComparison.Ordinal)));
            _logger?.Write("INFO", $"Camera {_settings.Slot} connected: {RtspUrlSanitizer.Redact(_settings.RtspUrl)}; transport={_settings.EffectiveTransport}; cache={_settings.EffectiveNetworkCacheMilliseconds} ms; lowLatency={_settings.EffectiveLowLatency}; videoOutput={(_useCompositedOutput ? "WPF frames / software decode" : "default")}; mediaOptions=[{effectiveOptions}]");
        };
        player.EndReached += (_, _) => { if (!_disposed && !_resolving && ReferenceEquals(_player, player)) ScheduleRecovery("Stream ended"); };
        player.EncounteredError += (_, _) => { if (!_disposed && !_resolving && ReferenceEquals(_player, player)) ScheduleRecovery("LibVLC reported a decoder or stream error"); };
    }

    private string? _wallSizing;
    private double _outputTileWidth;

    public void SetWallSizing(WallTile tile, double outputTileWidth)
    {
        _wallSizing = tile.Sizing;
        _videoZoomPercent = tile.ZoomPercent;
        _imageHorizontalPositionPercent = tile.HorizontalPositionPercent;
        _imageVerticalPositionPercent = tile.VerticalPositionPercent;
        _outputTileWidth = outputTileWidth;
        ApplyVideoSizing(_player);
    }

    private void ApplyVideoSizing(MediaPlayer? player)
    {
        if (_disposed || (player is not null && !ReferenceEquals(player, _player))) return;
        if (player is null && _sharedSource is null) return;
        if (player is not null && !_useCompositedOutput) player.Scale = 0;
        if (_wallSizing is not null)
        {
            _videoDisplayWidth = Math.Max(1, (int)VideoView.ActualWidth);
            _videoDisplayHeight = Math.Max(1, (int)VideoView.ActualHeight);
        }
        var desiredAspect = _wallSizing == "stretch" ? $"{_videoDisplayWidth}:{_videoDisplayHeight}" : null;
        if (player is not null && !_useCompositedOutput && (player.AspectRatio ?? "") != (desiredAspect ?? "")) player.AspectRatio = desiredAspect;
        if (player is not null && !_useCompositedOutput && !string.IsNullOrEmpty(player.CropGeometry)) player.CropGeometry = string.Empty;
        var dimensions = GetVideoDimensions();
        if (dimensions is not { } source)
            return;

        // Original sources live directly in the wall grid, not in an overlay
        // viewport. Use their actual WPF size (DIPs), including after reveal.
        if (_useCompositedOutput && _preserveWholeFrame)
        {
            var width = CompositedCanvas.ActualWidth;
            var height = CompositedCanvas.ActualHeight;
            if (width <= 0 || height <= 0 || source.Width == 0 || source.Height == 0) return;
            var wallImage = WallVideoTransform.Calculate(source.Width, source.Height, width, height,
                _wallSizing ?? "fit", _outputTileWidth, _videoZoomPercent, _imageHorizontalPositionPercent, _imageVerticalPositionPercent);
            CompositedImage.Width = wallImage.RenderWidth;
            CompositedImage.Height = wallImage.RenderHeight;
            System.Windows.Controls.Canvas.SetLeft(CompositedImage, wallImage.OffsetX);
            System.Windows.Controls.Canvas.SetTop(CompositedImage, wallImage.OffsetY);
            return;
        }
        if (_videoDisplayWidth <= 0 || _videoDisplayHeight <= 0) return;

        _lastSizingSourceWidth = source.Width;
        _lastSizingSourceHeight = source.Height;
        if (_useCompositedOutput)
        {
            var layout = DoorbellVideoTransform.CalculateLayout(source.Width, source.Height,
                _overlayReferenceWidth > 0 ? _overlayReferenceWidth : _videoDisplayWidth,
                _overlayReferenceHeight > 0 ? _overlayReferenceHeight : _videoDisplayHeight, _videoZoomPercent,
                _imageHorizontalPositionPercent, _imageVerticalPositionPercent);
            var scaleX = _overlayReferenceWidth > 0 ? _videoDisplayWidth / _overlayReferenceWidth : 1;
            var scaleY = _overlayReferenceHeight > 0 ? _videoDisplayHeight / _overlayReferenceHeight : 1;
            CompositedImage.Width = layout.RenderWidth * scaleX;
            CompositedImage.Height = layout.RenderHeight * scaleY;
            System.Windows.Controls.Canvas.SetLeft(CompositedImage, layout.OffsetX * scaleX);
            System.Windows.Controls.Canvas.SetTop(CompositedImage, layout.OffsetY * scaleY);
            return;
        }
        VideoView.ApplyTemplate();
        if (VideoView.Template.FindName("PART_PlayerHost", VideoView) is HwndHost videoHost)
        {
            var layoutApplied = NativeVideoSurfaceLayout.Apply(
                videoHost,
                (int)source.Width,
                (int)source.Height,
                _videoZoomPercent,
                _imageHorizontalPositionPercent,
                _imageVerticalPositionPercent, _wallSizing, _outputTileWidth);
            if (layoutApplied && !_nativeVideoLayoutConfirmed)
            {
                _nativeVideoLayoutConfirmed = true;
                _logger?.Write("INFO", $"Camera {_settings.Slot} ({_settings.Name}): native video viewport layout active; source={source.Width}x{source.Height}; viewport={_videoDisplayWidth}x{_videoDisplayHeight}; zoom={_videoZoomPercent}%; focus={_imageHorizontalPositionPercent},{_imageVerticalPositionPercent}");
            }
        }
    }

    private async void StartPlayer(bool recreatePlayer)
    {
        if (_disposed || _libVlc is null) return;
        if (!StreamSource.IsValidUrl(_settings.RtspUrl))
        {
            ScheduleRecovery("Invalid stream URL");
            return;
        }

        // Initial tiles may be collapsed when Initialize is called. LibVLCSharp
        // can then leave MediaPlayer.Hwnd at zero even after the host loads.
        // Never start native playback until a real destination exists.
        var playbackHandle = _useCompositedOutput ? IntPtr.Zero : GetNativeVideoHandle();
        if (!_useCompositedOutput && (!VideoView.IsLoaded || playbackHandle == IntPtr.Zero))
        {
            _pendingNativeStart = true;
            _pendingNativeRecreate |= recreatePlayer;
            SetState(CameraConnectionState.Connecting, "Waiting for video display…");
            return;
        }
        recreatePlayer |= _pendingNativeRecreate;
        _pendingNativeStart = _pendingNativeRecreate = false;
        CancelResolution();
        using var resolution = new CancellationTokenSource();
        _resolutionCancellation = resolution;
        _resolving = true;
        _status = _status with { NextReconnectAt = null };
        SetState(StreamSource.NeedsResolver(_settings) ? CameraConnectionState.Resolving : CameraConnectionState.Connecting,
            StreamSource.NeedsResolver(_settings) ? "Opening website stream…" : "Connecting…");
        ResolvedStream resolved;
        try { resolved = await StreamResolver.ResolveAsync(_settings, resolution.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception error)
        {
            if (!resolution.IsCancellationRequested && !_disposed) ScheduleRecovery(error.Message);
            return;
        }
        finally
        {
            if (ReferenceEquals(_resolutionCancellation, resolution))
            {
                _resolutionCancellation = null;
                _resolving = false;
            }
        }
        if (_disposed || resolution.IsCancellationRequested) { resolved.Dispose(); return; }
        _streamLease = resolved;
        var uri = resolved.Uri;
        var oldPlayer = _player;
        var oldMedia = _media;
        var oldPresenter = recreatePlayer ? _compositedPresenter : null;
        oldPresenter?.Deactivate();
        if (recreatePlayer)
        {
            _player = null;
            VideoView.MediaPlayer = null;
            CreatePlayer();
            _logger?.Write("WARNING", $"Camera {_settings.Slot}: recreated decoder/player after repeated failures");
        }

        var newPlayer = _player;
        _media = new Media(_libVlc, uri);
        var newMedia = _media;
        var recoveringFromFailure = _status.ConsecutiveFailures > 0;
        foreach (var option in _settings.ToMediaOptions()) _media.AddOption(option);
        if (_useCompositedOutput) _media.AddOption(":avcodec-hw=none");
        _attemptStartedAt = DateTimeOffset.UtcNow;
        _playGeneration++;
        _healthySince = null;
        _activity.BeginAttempt(_attemptStartedAt);
        _decoder = _useCompositedOutput || !_requestHardwareDecoding ? "Software decoding" : "Hardware requested (unconfirmed)";
        _lastLostPictures = -1;
        _pendingLostPictures = 0;
        _lastFrameLossLogAt = DateTimeOffset.MinValue;
        _status = _status with { NextReconnectAt = null };
        SetState(_status.ReconnectCount > 0 ? CameraConnectionState.Reconnecting : CameraConnectionState.Connecting);
        void QueuePlayback() => QueuePlayerOperation(() =>
        {
            try
            {
                // A native player that has reported an error may block indefinitely
                // in Stop(). Replaying it does not require waiting for Stop; after
                // escalation, release the poisoned instance independently.
                if (!recoveringFromFailure && oldPlayer is not null) lock (oldPlayer) oldPlayer.Stop();
                oldMedia?.Dispose();
                if (recreatePlayer) _ = Task.Run(() => { if (oldPlayer is not null) lock (oldPlayer) oldPlayer.Dispose(); oldPresenter?.Dispose(); });
                if (newPlayer is not null && !_useCompositedOutput)
                {
                    newPlayer.Hwnd = playbackHandle;
                    _logger?.Write("INFO", $"Camera {_settings.Slot}: native playback attached to HWND 0x{playbackHandle.ToInt64():X}");
                }
                if (newPlayer is not null && !newPlayer.Play(newMedia))
                    Dispatcher.BeginInvoke(() => ScheduleRecovery("LibVLC rejected the stream startup request"));
            }
            catch (Exception exception)
            {
                Dispatcher.BeginInvoke(() => ScheduleRecovery($"Stream startup failed: {exception.Message}"));
            }
        });

        QueuePlayback();
    }

    public Task<bool> RefreshSnapshotAsync()
    {
        if (_sharedSource is not null) return RefreshSharedSnapshotAsync();
        var player = _player;
        return player is null
            ? Task.FromResult(false)
            : CaptureSnapshotAsync(player, _playGeneration, waitForFirstFrame: false);
    }

    private async Task<bool> CaptureSnapshotAsync(MediaPlayer player, int generation, bool waitForFirstFrame)
    {
        string? temporaryPath = null;
        try
        {
            var path = Path.Combine(_snapshotDirectory, $"camera-{_settings.Slot}.jpg");
            var maximumAttempts = waitForFirstFrame ? 6 : 3;
            for (var attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                var delay = waitForFirstFrame
                    ? TimeSpan.FromSeconds(attempt == 1 ? 2 : 3)
                    : TimeSpan.FromMilliseconds(attempt == 1 ? 0 : 350);
                if (delay > TimeSpan.Zero) await Task.Delay(delay);
                var attemptPath = Path.Combine(_snapshotDirectory, $"camera-{_settings.Slot}-{Guid.NewGuid():N}.jpg");
                temporaryPath = attemptPath;
                // Snapshot requests can resume on worker threads. Serialize native calls
                // with release so a successful lifetime check cannot race player disposal.
                if (_useCompositedOutput)
                {
                    if (_disposed || generation != _playGeneration || !ReferenceEquals(_player, player)) return false;
                    var frame = _compositedPresenter is { } presenter ? await presenter.CaptureFrameAsync() : null;
                    if (frame is null) continue;
                    await Task.Run(() =>
                    {
                        var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(frame));
                        using var output = File.Create(attemptPath);
                        encoder.Save(output);
                    });
                }
                else
                {
                    if (!Monitor.TryEnter(player)) continue;
                    try
                    {
                        if (_disposed || generation != _playGeneration || !ReferenceEquals(_player, player) || !player.IsPlaying) return false;
                        using var snapshotMedia = player.Media;
                        if (snapshotMedia?.Statistics is { } statistics && statistics.DecodedVideo == 0) continue;
                        if (!player.TakeSnapshot(0, attemptPath, 320, 0))
                        {
                            DeleteTemporarySnapshot(attemptPath);
                            temporaryPath = null;
                            continue;
                        }
                    }
                    finally { Monitor.Exit(player); }
                }
                for (var check = 0; check < 20; check++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                    if (_disposed || generation != _playGeneration || !ReferenceEquals(_player, player)) return false;
                    try
                    {
                        using (var completedSnapshot = new FileStream(attemptPath, FileMode.Open, FileAccess.Read, FileShare.None))
                            if (completedSnapshot.Length == 0) continue;
                        File.Move(attemptPath, path, overwrite: true);
                        Interlocked.Exchange(ref _snapshotCapturedTicks, DateTimeOffset.UtcNow.Ticks);
                        temporaryPath = null;
                        return true;
                    }
                    catch (IOException) { }
                }
                DeleteTemporarySnapshot(attemptPath);
                temporaryPath = null;
            }
            _logger?.Write("WARNING", $"Camera {_settings.Slot}: thumbnail capture was not available after retries");
            return false;
        }
        catch (Exception exception)
        {
            _logger?.Write("WARNING", $"Camera {_settings.Slot}: thumbnail capture failed: {exception.Message}");
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
                DeleteTemporarySnapshot(temporaryPath);
        }
    }

    private static void DeleteTemporarySnapshot(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ScheduleRecovery(string error)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ScheduleRecovery(error));
            return;
        }
        if (_disposed || _status.NextReconnectAt is not null) return;
        CancelResolution();
        var failures = _status.ConsecutiveFailures + 1;
        var backoffSeconds = _settings.ReconnectDelaySeconds(failures);
        var jitterMilliseconds = Random.Shared.Next(0, 750);
        var now = DateTimeOffset.UtcNow;
        _status = _status with
        {
            State = CameraConnectionState.Offline,
            ConsecutiveFailures = failures,
            ReconnectCount = _status.ReconnectCount + 1,
            LastReconnectAt = now,
            NextReconnectAt = now.AddSeconds(Math.Min(Math.Clamp(_settings.MaximumReconnectBackoffSeconds, 5, 300), backoffSeconds + jitterMilliseconds / 1000d)),
            LastError = RollingFileLogger.RedactCredentials(error)
        };
        SetOverlay("Camera Offline", true);
        _logger?.Write("WARNING", $"Camera {_settings.Slot}: {error}; retry {failures} scheduled in {backoffSeconds:0}s");
    }

    private void Stop(CameraConnectionState state)
    {
        CancelResolution();
        _pendingNativeStart = _pendingNativeRecreate = false;
        var player = _player;
        var media = _media;
        _media = null;
        QueuePlayerOperation(() => { if (player is not null) lock (player) player.Stop(); media?.Dispose(); });
        _status = _status with { State = state, NextReconnectAt = null };
        SetState(state);
    }

    private void SetState(CameraConnectionState state, string? display = null)
    {
        _status = _status with { State = state };
        var showCenter = state is not CameraConnectionState.Live;
        SetOverlay(display ?? StateLabel(state), showCenter);
    }

    private static string StateLabel(CameraConnectionState state) => state switch
    {
        CameraConnectionState.Resolving => "Opening website stream",
        CameraConnectionState.NotConfigured => "Not configured",
        CameraConnectionState.StreamError => "Stream Error",
        CameraConnectionState.Offline => "Camera Offline",
        _ => state.ToString() + (state is CameraConnectionState.Connecting or CameraConnectionState.Reconnecting ? "…" : string.Empty)
    };

    private void SetOverlay(string text, bool showCenter)
    {
        void Update()
        {
            if (_disposed) return;
            DetailsText.Text = text;
            StateText.Text = text;
            StateText.Visibility = showCenter && !SharedDiagnostics ? Visibility.Visible : Visibility.Collapsed;
            SlotText.Visibility = showCenter && !SharedDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        }
        if (Dispatcher.CheckAccess()) Update();
        else Dispatcher.BeginInvoke(Update);
    }

    private void QueuePlayerOperation(Action operation)
    {
        lock (_operationGate)
            _playerOperation = _playerOperation.ContinueWith(_ => operation(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelResolution();
        foreach (var mirror in _frameMirrors.ToArray()) mirror.CompositedImage.Source = null;
        if (_sharedSource is not null) { _sharedSource._frameMirrors.Remove(this); _sharedSource._compositedPresenter?.RemoveMirror(CompositedImage); CompositedImage.Source = null; return; }
        _backgroundSource?.RemoveHook(ColorNativeVideoBackground);
        _backgroundSource = null;
        var presenter = _compositedPresenter;
        presenter?.Deactivate();
        var player = _player;
        var media = _media;
        var engine = _libVlc;
        _player = null; _media = null;
        QueuePlayerOperation(() => { if (player is not null) lock (player) { player.Stop(); player.Dispose(); } media?.Dispose(); presenter?.Dispose(); if (_ownsEngine) engine?.Dispose(); });
        try { _playerOperation.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
    }
}
