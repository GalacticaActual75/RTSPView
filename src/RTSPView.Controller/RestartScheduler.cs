using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Controller;

public sealed record RestartScheduleState
{
    public RestartScheduleSettings Settings { get; init; } = new();
    public DateTimeOffset? NextRun { get; init; }
    public DateTimeOffset? PendingUntil { get; init; }
    public DateTimeOffset? LastRun { get; init; }
    public string LastResult { get; init; } = "No attempts yet.";
    public string Result { get; init; } = "No scheduled restarts yet.";
}

// The host-local file deliberately lives outside portable stream configuration backups.
public sealed class RestartScheduler : BackgroundService
{
    private readonly string _path;
    private readonly Func<string, CancellationToken, Task<string>> _execute;
    private readonly Func<Func<Task>, CancellationToken, Task<bool>> _maintenance;
    private readonly Action<string> _log;
    private readonly TimeZoneInfo _zone;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RestartScheduleState _state;

    public RestartScheduler(string directory, Func<string, CancellationToken, Task<string>> execute,
        Func<Func<Task>, CancellationToken, Task<bool>> maintenance, Action<string> log,
        TimeZoneInfo? zone = null, DateTimeOffset? now = null)
    {
        _path = Path.Combine(directory, "restart-schedule.json");
        (_execute, _maintenance, _log, _zone) = (execute, maintenance, log, zone ?? TimeZoneInfo.Local);
        _state = new();
        if (File.Exists(_path))
        {
            try
            {
                _state = JsonSerializer.Deserialize<RestartScheduleState>(File.ReadAllText(_path)) ?? throw new InvalidDataException();
                if (_state.Settings is null) throw new InvalidDataException();
                _state.Settings.Validate();
            }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidDataException or UnauthorizedAccessException)
            {
                _state = new() { Result = "Schedule could not be read; automatic restarts are disabled. Save settings to recover." };
                _log(_state.Result);
            }
        }
        var current = now ?? DateTimeOffset.UtcNow;
        if (_state.PendingUntil is not null || _state.NextRun <= current || (_state.Settings.Enabled && _state.NextRun is null))
        {
            try { Save(_state with { NextRun = _state.Settings.NextAfter(current, _zone), PendingUntil = null,
                Result = "Missed or interrupted restart skipped at Controller startup." }); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _state = _state with { Settings = _state.Settings with {Enabled=false}, NextRun=null, PendingUntil=null,
                    Result="Schedule state could not be saved; automatic restarts are disabled until settings are saved successfully." };
                _log(_state.Result);
            }
        }
    }

    public async Task<RestartScheduleState> ReadAsync()
    {
        await _gate.WaitAsync();
        try { return _state with { Settings = _state.Settings with { Days = _state.Settings.Days.ToArray() } }; }
        finally { _gate.Release(); }
    }

    public async Task ConfigureAsync(RestartScheduleSettings settings, bool hostAcknowledged, DateTimeOffset now)
    {
        settings.Validate();
        if (settings.Enabled && settings.Action == "host" && !hostAcknowledged)
            throw new ArgumentException("Acknowledge that this schedule restarts the entire Windows host.");
        await _gate.WaitAsync();
        try
        {
            Save(_state with { Settings = settings with { Days = settings.Days.Distinct().ToArray() },
                NextRun = settings.NextAfter(now, _zone), PendingUntil = null,
                Result = settings.Enabled ? "Schedule saved." : "Schedule disabled." });
            _log(_state.Result);
        }
        finally { _gate.Release(); }
    }

    public async Task SkipAsync(DateTimeOffset now)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_state.Settings.Enabled || _state.NextRun is null) return;
            var next = _state.PendingUntil is null && _state.NextRun > now ? _state.NextRun.Value : now;
            Save(_state with { NextRun = _state.Settings.NextAfter(next, _zone), PendingUntil = null,
                Result = "Scheduled restart skipped by administrator." });
            _log(_state.Result);
        }
        finally { _gate.Release(); }
    }

    public async Task TickAsync(DateTimeOffset now, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!_state.Settings.Enabled || _state.NextRun is null || _state.NextRun > now) return;
            if (_state.PendingUntil is { } pending && pending > now) return;
            // Sleep/resume or a long scheduler interruption must not cause a surprise catch-up reboot.
            if ((_state.PendingUntil ?? _state.NextRun) < now.AddMinutes(-2))
            {
                Save(_state with { NextRun = _state.Settings.NextAfter(now, _zone), PendingUntil = null,
                    Result = "Missed restart skipped after host inactivity." });
                return;
            }
            var ran = await _maintenance(async () =>
            {
                if (_state.Settings.Action == "host" && _state.PendingUntil is null)
                {
                    Save(_state with { PendingUntil = now.AddSeconds(60), Result = "Windows host restart in 60 seconds. Cancel to skip this run." });
                    _log(_state.Result);
                    return;
                }
                var action = _state.Settings.Action;
                // Persist consumption before the side effect: a crash cannot replay this occurrence.
                Save(_state with { NextRun = _state.Settings.NextAfter(now, _zone), PendingUntil = null,
                    LastRun = now, Result = $"Scheduled {action} restart requested; completion not yet confirmed.",
                    LastResult = $"Scheduled {action} restart requested; completion not yet confirmed." });
                try { var result = await _execute(action, token); Save(_state with { Result = result, LastResult = result }); }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    var result = $"Scheduled {action} restart failed ({error.GetType().Name}). Check host permissions and logs.";
                    Save(_state with { Result = result, LastResult = result });
                }
                _log(_state.Result);
            }, token);
            if (!ran)
            {
                // Retry after the update, with a fresh countdown for host restarts.
                Save(_state with { NextRun = now.AddMinutes(1), PendingUntil = null,
                    Result = "Restart deferred while an RTSPView update is active." });
            }
        }
        finally { _gate.Release(); }
    }

    private void Save(RestartScheduleState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state));
        File.Move(temp, _path, true);
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            try { await TickAsync(DateTimeOffset.UtcNow, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { _log($"Restart scheduler could not advance ({error.GetType().Name}); no unrecorded restart is allowed."); }
        }
    }
}

public sealed record RestartScheduleRequest(RestartScheduleSettings Settings, bool HostAcknowledged);
