using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using SpotMonitor.Core;
using SpotMonitor.Infrastructure;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SpotMonitor.Viewer;

public partial class CameraTile : System.Windows.Controls.UserControl, IDisposable
{
    private MediaPlayer? _player;
    private Media? _media;
    private LibVLC? _libVlc;
    private RollingFileLogger? _logger;
    private CameraSettings _settings = new();
    private CameraRuntimeStatus _status = new();
    private DateTimeOffset _attemptStartedAt;
    private DateTimeOffset? _healthySince;
    private int _lastDecodedFrames = -1;
    private long _lastLostPictures = -1;
    private long _pendingLostPictures;
    private DateTimeOffset _lastFrameLossLogAt = DateTimeOffset.MinValue;
    private bool _requestHardwareDecoding;
    private bool _disposed;
    private bool _showCameraNames = true;
    private bool _showCameraStats = true;
    private readonly object _operationGate = new();
    private Task _playerOperation = Task.CompletedTask;
    private int _playGeneration;
    private nint _nativeVideoHandle;
    private readonly string _snapshotDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpotMonitor", "snapshots");

    public int Slot => _settings.Slot;
    public bool IsPlaying => _player?.IsPlaying == true;
    public CameraRuntimeStatus Status => _status;
    public event EventHandler? PointerActivity;

    public CameraTelemetry GetTelemetry()
    {
        var stats = _player?.Media?.Statistics;
        var videoTrack = _media?.Tracks.FirstOrDefault(track => track.TrackType == TrackType.Video);
        return new CameraTelemetry
        {
            Slot = _settings.Slot,
            Name = _settings.Name,
            State = _status.State.ToString(),
            Fps = _player?.Fps ?? 0,
            BitrateKbps = stats is null ? 0 : Math.Round(stats.Value.DemuxBitrate * 8000, 1),
            Codec = videoTrack is null ? null : FourCc(videoTrack.Value.Codec),
            Width = videoTrack?.Data.Video.Width,
            Height = videoTrack?.Data.Video.Height,
            ReconnectCount = _status.ReconnectCount,
            StreamUptimeSeconds = _status.ConnectedAt is null ? null : (long)(DateTimeOffset.UtcNow - _status.ConnectedAt.Value).TotalSeconds,
            StreamStartedAt = _status.ConnectedAt,
            LastFrameAt = _status.LastFrameAt,
            LastReconnectAt = _status.LastReconnectAt,
            LastError = _status.LastError
        };
    }

    private static string FourCc(uint value) => new string([(char)(value & 0xff), (char)((value >> 8) & 0xff), (char)((value >> 16) & 0xff), (char)((value >> 24) & 0xff)]).Trim('\0').ToUpperInvariant();

    public CameraTile() => InitializeComponent();

    public void Initialize(LibVLC libVlc, RollingFileLogger logger, CameraSettings settings, bool requestHardwareDecoding)
    {
        _libVlc = libVlc;
        _logger = logger;
        _settings = settings;
        _requestHardwareDecoding = requestHardwareDecoding;
        Directory.CreateDirectory(_snapshotDirectory);
        NameText.Text = settings.Name;
        SlotText.Text = $"Slot {settings.Slot}";
        UpdateRestartButton();
        CreatePlayer();
        if (settings.Enabled && !string.IsNullOrWhiteSpace(settings.RtspUrl)) Start(manual: true);
        else SetState(settings.Enabled ? CameraConnectionState.NotConfigured : CameraConnectionState.Disabled);
    }

    public void Apply(CameraSettings settings)
    {
        _settings = settings;
        NameText.Text = settings.Name;
        SlotText.Text = $"Slot {settings.Slot}";
        UpdateRestartButton();
        if (settings.Enabled && !string.IsNullOrWhiteSpace(settings.RtspUrl)) Start(manual: true);
        else Stop(settings.Enabled ? CameraConnectionState.NotConfigured : CameraConnectionState.Disabled);
    }

    public void ApplyOverlayPreferences(bool showCameraNames, bool showCameraStats)
    {
        _showCameraNames = showCameraNames;
        _showCameraStats = showCameraStats;
        UpdateOverlayPresentation(DateTimeOffset.UtcNow, "Hardware decode");
    }

    public void Start(bool manual = true)
    {
        if (_disposed || _libVlc is null) return;
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
        RestartStreamButton.Visibility = Visibility.Visible;
        PointerActivity?.Invoke(this, EventArgs.Empty);
    }

    private void OverlayRoot_MouseMove(object sender, MouseEventArgs e) =>
        PointerActivity?.Invoke(this, EventArgs.Empty);

    private void OverlayRoot_MouseLeave(object sender, MouseEventArgs e) =>
        RestartStreamButton.Visibility = Visibility.Collapsed;

    public void HideHoverControls() => RestartStreamButton.Visibility = Visibility.Collapsed;

    public void EnsureNativeVideoBackground(bool forceRedraw = false)
    {
        VideoView.ApplyTemplate();
        if (VideoView.Template.FindName("PART_PlayerHost", VideoView) is not HwndHost videoHost ||
            videoHost.Handle == IntPtr.Zero)
            return;

        var handle = (nint)videoHost.Handle;
        var handleChanged = handle != _nativeVideoHandle;
        _nativeVideoHandle = handle;
        NativeVideoBackgroundGuard.Apply(handle, forceRedraw || handleChanged);
    }

    private void UpdateRestartButton() => RestartStreamButton.IsEnabled =
        _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.RtspUrl);

    public void Tick(string hardwareDecoder)
    {
        if (_disposed || !_settings.Enabled) return;
        var now = DateTimeOffset.UtcNow;
        UpdateOverlayPresentation(now, hardwareDecoder);

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

        if (_status.State is CameraConnectionState.Connecting or CameraConnectionState.Buffering &&
            now - _attemptStartedAt > TimeSpan.FromSeconds(Math.Clamp(_settings.StartupTimeoutSeconds, 8, 120)))
        {
            ScheduleRecovery("Stream startup timed out");
            return;
        }

        if (_player?.IsPlaying == true)
        {
            ObserveFrameProgress(now);
            if (_status.LastFrameAt is not null &&
                now - _status.LastFrameAt > TimeSpan.FromSeconds(Math.Clamp(_settings.WatchdogTimeoutSeconds, 8, 120)))
            {
                ScheduleRecovery("No decoded-frame progress; watchdog detected a stalled stream");
                return;
            }

            if (_healthySince is not null && now - _healthySince > TimeSpan.FromSeconds(60) && _status.ConsecutiveFailures > 0)
                _status = _status with { ConsecutiveFailures = 0 };

            UpdateOverlayPresentation(now, hardwareDecoder);
        }
        else UpdateOverlayPresentation(now, hardwareDecoder);
    }

    private void UpdateOverlayPresentation(DateTimeOffset now, string hardwareDecoder)
    {
        var connectionNeedsAttention = _settings.Enabled && _status.State is not CameraConnectionState.Live and not CameraConnectionState.Disabled and not CameraConnectionState.NotConfigured;
        var recentlyRecovered = _status.State == CameraConnectionState.Live && _healthySince is not null && now - _healthySince < TimeSpan.FromSeconds(15);
        var forceVisible = connectionNeedsAttention || recentlyRecovered;
        var showName = _showCameraNames || forceVisible;
        var showStats = _showCameraStats || forceVisible;

        NameText.Visibility = showName ? Visibility.Visible : Visibility.Collapsed;
        if (showStats)
        {
            if (_status.State == CameraConnectionState.Live && _player is not null)
            {
                var stats = _player.Media?.Statistics;
                var bitrate = stats is null ? string.Empty : $" • {stats.Value.DemuxBitrate * 8000:0} kb/s";
                var uptime = _status.ConnectedAt is null ? string.Empty : $" • {(now - _status.ConnectedAt.Value):hh\\:mm\\:ss}";
                DetailsText.Text = $"{_player.Fps:0.0} fps{bitrate} • {hardwareDecoder} • R:{_status.ReconnectCount}{uptime}";
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
    }

    private void ObserveFrameProgress(DateTimeOffset now)
    {
        var stats = _player?.Media?.Statistics;
        if (stats is null) return;
        var decodedFrames = stats.Value.DecodedVideo;
        if (decodedFrames != _lastDecodedFrames)
        {
            _lastDecodedFrames = decodedFrames;
            _status = _status with { LastFrameAt = now };
        }

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
        var player = new MediaPlayer(_libVlc) { EnableHardwareDecoding = _requestHardwareDecoding };
        _player = player;
        VideoView.MediaPlayer = player;
        player.Opening += (_, _) => { if (ReferenceEquals(_player, player)) SetState(CameraConnectionState.Connecting); };
        player.Buffering += (_, e) =>
        {
            if (!ReferenceEquals(_player, player)) return;
            if (e.Cache >= 99.5f && player.IsPlaying) SetState(CameraConnectionState.Live);
            else SetState(CameraConnectionState.Buffering, $"Buffering {e.Cache:0}%");
        };
        player.Playing += (_, _) =>
        {
            if (!ReferenceEquals(_player, player)) return;
            var now = DateTimeOffset.UtcNow;
            _healthySince = now;
            _lastDecodedFrames = -1;
            _status = _status with { State = CameraConnectionState.Live, ConnectedAt = now, LastFrameAt = now, NextReconnectAt = null };
            var generation = _playGeneration;
            _ = CaptureSnapshotAsync(player, generation);
            SetOverlay("Live", false);
            var effectiveOptions = string.Join(' ', _settings.ToMediaOptions().Where(option => option.StartsWith(":rtsp-", StringComparison.Ordinal) || option.StartsWith(":network-caching=", StringComparison.Ordinal)));
            _logger?.Write("INFO", $"Camera {_settings.Slot} connected: {RtspUrlSanitizer.Redact(_settings.RtspUrl)}; transport={_settings.EffectiveTransport}; cache={_settings.EffectiveNetworkCacheMilliseconds} ms; lowLatency={_settings.EffectiveLowLatency}; mediaOptions=[{effectiveOptions}]");
        };
        player.EndReached += (_, _) => { if (ReferenceEquals(_player, player)) ScheduleRecovery("Stream ended"); };
        player.EncounteredError += (_, _) => { if (ReferenceEquals(_player, player)) ScheduleRecovery("LibVLC reported a decoder or stream error"); };
    }

    private void StartPlayer(bool recreatePlayer)
    {
        if (_libVlc is null) return;
        if (!Uri.TryCreate(_settings.RtspUrl, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
        {
            ScheduleRecovery("Invalid RTSP URL");
            return;
        }

        var oldPlayer = _player;
        var oldMedia = _media;
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
        _attemptStartedAt = DateTimeOffset.UtcNow;
        _playGeneration++;
        _healthySince = null;
        _lastDecodedFrames = -1;
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
                if (!recoveringFromFailure) oldPlayer?.Stop();
                oldMedia?.Dispose();
                if (recreatePlayer) _ = Task.Run(() => oldPlayer?.Dispose());
                if (newPlayer is not null && !newPlayer.Play(newMedia))
                    Dispatcher.BeginInvoke(() => ScheduleRecovery("LibVLC rejected the stream startup request"));
            }
            catch (Exception exception)
            {
                Dispatcher.BeginInvoke(() => ScheduleRecovery($"Stream startup failed: {exception.Message}"));
            }
        });

        // Assigning a replacement MediaPlayer causes LibVLCSharp.WPF to attach
        // the tile's native HWND asynchronously. Starting sooner can make VLC
        // fall back to a separate "VLC Direct Output" window. Let WPF finish
        // loaded/render work before the native Play call is queued.
        if (recreatePlayer)
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, async () =>
            {
                await Task.Delay(250);
                QueuePlayback();
            });
        else
            QueuePlayback();
    }

    private async Task CaptureSnapshotAsync(MediaPlayer player, int generation)
    {
        try
        {
            var path = Path.Combine(_snapshotDirectory, $"camera-{_settings.Slot}.jpg");
            var captureStartedAt = DateTime.UtcNow;
            for (var attempt = 1; attempt <= 6; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt == 1 ? 2 : 3));
                if (_disposed || generation != _playGeneration || !ReferenceEquals(_player, player) || !player.IsPlaying) return;
                if (player.Media?.Statistics is { } statistics && statistics.DecodedVideo == 0) continue;
                if (!player.TakeSnapshot(0, path, 320, 180)) continue;
                await Task.Delay(TimeSpan.FromSeconds(1));
                if (File.Exists(path) && File.GetLastWriteTimeUtc(path) >= captureStartedAt) return;
            }
            _logger?.Write("WARNING", $"Camera {_settings.Slot}: thumbnail capture was not available after retries");
        }
        catch (Exception exception) { _logger?.Write("WARNING", $"Camera {_settings.Slot}: thumbnail capture failed: {exception.Message}"); }
    }

    private void ScheduleRecovery(string error)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ScheduleRecovery(error));
            return;
        }
        if (_disposed || _status.NextReconnectAt is not null) return;
        var failures = _status.ConsecutiveFailures + 1;
        var backoffSeconds = Math.Min(Math.Clamp(_settings.MaximumReconnectBackoffSeconds, 5, 300), Math.Pow(2, Math.Min(failures - 1, 5)));
        var jitterMilliseconds = Random.Shared.Next(0, 750);
        var now = DateTimeOffset.UtcNow;
        _status = _status with
        {
            State = CameraConnectionState.Offline,
            ConsecutiveFailures = failures,
            ReconnectCount = _status.ReconnectCount + 1,
            LastReconnectAt = now,
            NextReconnectAt = now.AddSeconds(backoffSeconds).AddMilliseconds(jitterMilliseconds),
            LastError = error
        };
        SetOverlay("Camera Offline", true);
        _logger?.Write("WARNING", $"Camera {_settings.Slot}: {error}; retry {failures} scheduled in {backoffSeconds:0}s");
    }

    private void Stop(CameraConnectionState state)
    {
        var player = _player;
        var media = _media;
        _media = null;
        QueuePlayerOperation(() => { player?.Stop(); media?.Dispose(); });
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
        CameraConnectionState.NotConfigured => "Not configured",
        CameraConnectionState.StreamError => "Stream Error",
        CameraConnectionState.Offline => "Camera Offline",
        _ => state.ToString() + (state is CameraConnectionState.Connecting or CameraConnectionState.Reconnecting ? "…" : string.Empty)
    };

    private void SetOverlay(string text, bool showCenter) => Dispatcher.Invoke(() =>
    {
        DetailsText.Text = text;
        StateText.Text = text;
        StateText.Visibility = showCenter ? Visibility.Visible : Visibility.Collapsed;
        SlotText.Visibility = showCenter ? Visibility.Visible : Visibility.Collapsed;
    });

    private void QueuePlayerOperation(Action operation)
    {
        lock (_operationGate)
            _playerOperation = _playerOperation.ContinueWith(_ => operation(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var player = _player;
        var media = _media;
        QueuePlayerOperation(() => { player?.Stop(); media?.Dispose(); player?.Dispose(); });
        try { _playerOperation.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
    }
}
