using System.Text.Json;
using RTSPView.Core;
using RTSPView.Infrastructure;

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

public sealed record RestartSchedulesState
{
    public string SelectedAction { get; init; } = "viewer";
    public RestartScheduleState Viewer { get; init; } = new();
    public RestartScheduleState Host { get; init; } = new() { Settings = new() { Action = "host" } };
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
    private RestartSchedulesState _schedules = new();
    private RestartScheduleState State(string action) => action switch
    {
        "viewer" => _schedules.Viewer,
        "host" => _schedules.Host,
        _ => throw new ArgumentException("Choose viewer or host.")
    };

    public RestartScheduler(string directory, Func<string, CancellationToken, Task<string>> execute,
        Func<Func<Task>, CancellationToken, Task<bool>> maintenance, Action<string> log,
        TimeZoneInfo? zone = null, DateTimeOffset? now = null)
    {
        _path = Path.Combine(directory, "restart-schedule.json");
        (_execute, _maintenance, _log, _zone) = (execute, maintenance, log, zone ?? TimeZoneInfo.Local);
        if (File.Exists(_path))
        {
            try
            {
                var json = File.ReadAllText(_path);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
                if (document.RootElement.TryGetProperty("Settings", out _))
                {
                    // Preserve the existing single schedule in its original action slot.
                    var legacy = JsonSerializer.Deserialize<RestartScheduleState>(json) ?? throw new InvalidDataException();
                    if (legacy.Settings is null) throw new InvalidDataException();
                    legacy.Settings.Validate();
                    _schedules = legacy.Settings.Action == "host"
                        ? new() { Host = legacy, SelectedAction = "host" } : new() { Viewer = legacy };
                }
                else
                    _schedules = JsonSerializer.Deserialize<RestartSchedulesState>(json) ?? throw new InvalidDataException();
                foreach (var action in new[] { "viewer", "host" })
                {
                    var state = State(action);
                    if (state?.Settings is null || state.Settings.Action != action) throw new InvalidDataException();
                    state.Settings.Validate();
                }
                _ = State(_schedules.SelectedAction);
            }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidDataException or UnauthorizedAccessException)
            {
                DurableJson.PreserveInvalid(_path);
                const string message = "Schedules could not be read; automatic restarts are disabled. Save settings to recover.";
                _schedules = new() { Viewer = new() { Result = message }, Host = new() { Settings = new() { Action = "host" }, Result = message } };
                _log(message);
            }
        }
        var current = now ?? DateTimeOffset.UtcNow;
        foreach (var action in new[] { "viewer", "host" })
        {
            var state = State(action);
            if (state.PendingUntil is not null || state.NextRun <= current || (state.Settings.Enabled && state.NextRun is null))
            {
                try { Save(state with { NextRun = state.Settings.NextAfter(current, _zone), PendingUntil = null,
                    Result = "Missed or interrupted restart skipped at Controller startup." }); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    var disabled = state with { Settings = state.Settings with {Enabled=false}, NextRun=null, PendingUntil=null,
                        Result="Schedule state could not be saved; automatic restarts are disabled until settings are saved successfully." };
                    _schedules = Replace(disabled);
                    _log(disabled.Result);
                }
            }
        }
    }

    private static RestartScheduleState Copy(RestartScheduleState state) => state with { Settings = state.Settings with { Days = state.Settings.Days.ToArray() } };

    public async Task<RestartSchedulesState> ReadAllAsync()
    {
        await _gate.WaitAsync();
        try { return _schedules with { Viewer = Copy(_schedules.Viewer), Host = Copy(_schedules.Host) }; }
        finally { _gate.Release(); }
    }

    public async Task<RestartScheduleState> ReadAsync(string? action = null)
    {
        await _gate.WaitAsync();
        try { return Copy(State(action ?? _schedules.SelectedAction)); }
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
            var state = State(settings.Action);
            Save(state with { Settings = settings with { Days = settings.Days.Distinct().ToArray() },
                NextRun = settings.NextAfter(now, _zone), PendingUntil = null,
                Result = settings.Enabled ? "Schedule saved." : "Schedule disabled." }, select: true);
            _log($"{settings.Action}: {State(settings.Action).Result}");
        }
        finally { _gate.Release(); }
    }

    public async Task SkipAsync(DateTimeOffset now, string? action = null)
    {
        await _gate.WaitAsync();
        try
        {
            var state = State(action ?? _schedules.SelectedAction);
            if (!state.Settings.Enabled || state.NextRun is null) return;
            var next = state.PendingUntil is null && state.NextRun > now ? state.NextRun.Value : now;
            Save(state with { NextRun = state.Settings.NextAfter(next, _zone), PendingUntil = null,
                Result = "Scheduled restart skipped by administrator." });
            _log($"{state.Settings.Action}: {State(state.Settings.Action).Result}");
        }
        finally { _gate.Release(); }
    }

    public async Task TickAsync(DateTimeOffset now, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            await TickActionAsync("host", now, token);
            await TickActionAsync("viewer", now, token);
        }
        finally { _gate.Release(); }
    }

    private async Task TickActionAsync(string scheduledAction, DateTimeOffset now, CancellationToken token)
    {
        var state = State(scheduledAction);
        if (!state.Settings.Enabled || state.NextRun is null || state.NextRun > now) return;
        if (state.PendingUntil is { } pending && pending > now) return;
        // Sleep/resume or a long scheduler interruption must not cause a surprise catch-up reboot.
        if ((state.PendingUntil ?? state.NextRun) < now.AddMinutes(-2))
        {
            Save(state with { NextRun = state.Settings.NextAfter(now, _zone), PendingUntil = null,
                Result = "Missed restart skipped after host inactivity." });
            return;
        }
        if (scheduledAction == "viewer" && (_schedules.Host.PendingUntil is not null || _schedules.Host.LastRun == now))
        {
            Save(state with { NextRun = state.Settings.NextAfter(now, _zone),
                Result = "Viewer restart skipped because a Windows host restart is pending or was just requested." });
            return;
        }
        var ran = await _maintenance(async () =>
        {
            if (state.Settings.Action == "host" && state.PendingUntil is null)
            {
                Save(state with { PendingUntil = now.AddSeconds(60), Result = "Windows host restart in 60 seconds. Cancel to skip this run." });
                _log(State(scheduledAction).Result);
                return;
            }
            var action = state.Settings.Action;
            // Persist consumption before the side effect: a crash cannot replay this occurrence.
            Save(state with { NextRun = state.Settings.NextAfter(now, _zone), PendingUntil = null,
                LastRun = now, Result = $"Scheduled {action} restart requested; completion not yet confirmed.",
                LastResult = $"Scheduled {action} restart requested; completion not yet confirmed." });
            try { var result = await _execute(action, token); Save(State(action) with { Result = result, LastResult = result }); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                var reference = Guid.NewGuid().ToString("N")[..8];
                var result = $"Scheduled {action} restart failed ({error.GetType().Name}). Reference {reference}. Open Settings → Maintenance.";
                Save(State(action) with { Result = result, LastResult = result });
            }
            _log(State(action).Result);
        }, token);
        if (!ran)
        {
            // Retry after the update, with a fresh countdown for host restarts.
            Save(state with { NextRun = now.AddMinutes(1), PendingUntil = null,
                Result = "Restart deferred while an RTSPView update is active." });
        }
    }

    private RestartSchedulesState Replace(RestartScheduleState state) => state.Settings.Action == "host"
        ? _schedules with { Host = state } : _schedules with { Viewer = state };

    private void Save(RestartScheduleState state, bool select = false)
    {
        var schedules = Replace(state);
        if (select) schedules = schedules with { SelectedAction = state.Settings.Action };
        DurableJson.Write(_path, schedules);
        _schedules = schedules;
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
