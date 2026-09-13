using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Controller;

public sealed record UpdateMonitorState
{
    public Dictionary<string, UpdateStatus> Channels { get; init; } = new();
    public bool ShowWallNotifications { get; init; } = true;
    public int Failures { get; init; }
}

public sealed class UpdateMonitor : BackgroundService
{
    private readonly string _path, _noticePath;
    private readonly Func<UpdateStatus> _initial;
    private readonly Func<CancellationToken, Task<UpdateStatus>> _check;
    private readonly Func<string, string, CancellationToken, Task<UpdateLaunchResult>> _install;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateMonitorState _state = new();
    private readonly Action<string> _log;
    private UpdateStatus? _published;

    public UpdateMonitor(string directory, Func<UpdateStatus> initial, Func<CancellationToken, Task<UpdateStatus>> check,
        Func<string, string, CancellationToken, Task<UpdateLaunchResult>> install, Action<string> log)
    {
        (_initial, _check, _install, _log) = (initial, check, install, log);
        _path = Path.Combine(directory, "update-monitor.json");
        _noticePath = Path.Combine(directory, "update-notice.json");
        try
        {
            if (File.Exists(_path)) _state = JsonSerializer.Deserialize<UpdateMonitorState>(File.ReadAllText(_path)) ?? new();
            if (_state.Channels is null) throw new InvalidDataException();
            foreach (var entry in _state.Channels)
            {
                UpdateRelease.ValidateChannel(entry.Key);
                if (entry.Value is null) throw new InvalidDataException();
                UpdateRelease.Parse(entry.Value.InstalledVersion);
                if(entry.Value.LatestVersion is not null)UpdateRelease.Parse(entry.Value.LatestVersion);
            }
        }
        catch (Exception error) when (error is IOException or JsonException)
        { _state=new(); _log("Update cache could not be read; it will be rebuilt."); }
        Publish(Current());
    }

    private UpdateStatus Current()
    {
        var initial = _initial();
        return (_state.Channels.TryGetValue(initial.SelectedChannel, out var saved) && saved.InstalledVersion == initial.InstalledVersion
            ? saved : initial) with { ShowWallNotifications = _state.ShowWallNotifications };
    }

    public async Task<UpdateStatus> StatusAsync()
    {
        if (!await _gate.WaitAsync(0)) return _published ?? _initial();
        try { var result = Current(); Publish(result); return result; }
        finally { _gate.Release(); }
    }

    public async Task<UpdateStatus> CheckAsync(bool manual, DateTimeOffset now, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var status = Current();
            // A manual click may bypass the daily timer, but never the two-minute minimum or server cooldown.
            if (status.RetryAt > now || (manual ? status.LastChecked > now.AddMinutes(-2) : status.NextCheck > now)) return status;
            status = status with { LastChecked = now, NextCheck = now.AddHours(24) };
            Save(status); // Persist before making any request, including across Controller restarts.
            var checkedStatus = await _check(token);
            var failed = checkedStatus.CheckFailed;
            _state = _state with { Failures = failed ? Math.Min(_state.Failures + 1, 6) : 0 };
            var next = failed ? now.AddHours(Math.Min(24, Math.Pow(2, _state.Failures - 1))) : now.AddHours(24);
            if (checkedStatus.RetryAt > next) next = checkedStatus.RetryAt.Value;
            status = checkedStatus with { LastChecked = now, NextCheck = next, ShowWallNotifications = _state.ShowWallNotifications };
            Save(status);
            _log(failed ? "Automatic update check unavailable; retry scheduled." : "Update check completed.");
            return status;
        }
        finally { _gate.Release(); }
    }

    public async Task<UpdateStatus> NotificationsAsync(bool enabled)
    {
        await _gate.WaitAsync();
        try { _state = _state with {ShowWallNotifications=enabled}; var status = Current(); Save(status); return status; }
        finally { _gate.Release(); }
    }

    public async Task<WallUpdateResponse> InstallFromWallAsync(WallUpdateRequest request, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var status = Current();
            if (!request.Confirmed || !Notice(status).Visible || request.Version != status.LatestVersion || request.Channel != status.SelectedChannel)
                return new(false, "The update selection changed or is unavailable. Check the System page for current status.");
            var result = await _install(request.Channel, request.Version, token);
            if(result.Started) Save(status with {UpdateAvailable=false,Message="Update installation started. Follow the progress window on the host."});
            return new(result.Started, result.Message);
        }
        finally { _gate.Release(); }
    }

    public static WallUpdateNotice Notice(UpdateStatus status)
    {
        var newer = false;
        if (status.LatestVersion is not null)
        {
            var installed = UpdateRelease.Parse(status.InstalledVersion);
            var latest = UpdateRelease.Parse(status.LatestVersion);
            var baseComparison = new Version(latest.Number.Major, latest.Number.Minor, latest.Number.Build)
                .CompareTo(new Version(installed.Number.Major, installed.Number.Minor, installed.Number.Build));
            newer = baseComparison > 0 || baseComparison == 0 &&
                (installed.Channel == "beta" && latest.Channel == "stable" || latest.Channel == installed.Channel && latest.Number > installed.Number);
        }
        return new(status.ShowWallNotifications && status.ChannelAvailable && status.UpdateAvailable && newer, status.LatestVersion, status.SelectedChannel);
    }

    private void Save(UpdateStatus status)
    {
        _state.Channels[status.SelectedChannel] = status;
        Write(_path, _state);
        Publish(status);
    }
    private void Publish(UpdateStatus status) { Write(_noticePath, Notice(status)); _published=status; }
    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(value);
        if (File.Exists(path) && File.ReadAllText(path) == json) return;
        File.WriteAllText(path + ".tmp", json); File.Move(path + ".tmp", path, true);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckAsync(false, DateTimeOffset.UtcNow, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { _log("Update monitor failed: " + error.GetType().Name); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
