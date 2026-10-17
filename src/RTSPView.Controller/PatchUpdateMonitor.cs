using System.Text.Json;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

public sealed record PatchUpdateStatus(string InstalledVersion, bool AutoInstall = false,
    string? AvailableVersion = null, DateTimeOffset? LastChecked = null, DateTimeOffset? NextCheck = null,
    string Message = "Waiting for the daily patch check.", DateTimeOffset? LastAttempt = null, string? LastResult = null);

// A separate preference: hiding release badges never changes installation consent.
public sealed class PatchUpdateMonitor : BackgroundService
{
    private readonly string _path;
    private readonly UpdateRelease _installed;
    private readonly Func<CancellationToken, Task<string?>> _check;
    private readonly Func<string, Func<bool>, CancellationToken, Task<UpdateLaunchResult>> _install;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private PatchUpdateStatus _state;

    public PatchUpdateMonitor(string directory, UpdateRelease installed, Func<CancellationToken, Task<string?>> check,
        Func<string, Func<bool>, CancellationToken, Task<UpdateLaunchResult>> install)
    {
        (_installed, _check, _install) = (installed, check, install);
        _path = Path.Combine(directory, "patch-updates.json");
        _state = new(installed.Label);
        try
        {
            if (!File.Exists(_path)) return;
            var saved = JsonSerializer.Deserialize<PatchUpdateStatus>(File.ReadAllText(_path)) ?? throw new InvalidDataException();
            if (saved.InstalledVersion == installed.Label)
            {
                if (saved.AvailableVersion is not null && !IsPatch(saved.AvailableVersion)) throw new InvalidDataException();
                _state = saved;
            }
            else _state = _state with { AutoInstall = saved.AutoInstall, LastAttempt = saved.LastAttempt,
                LastResult = saved.AvailableVersion == installed.Label ? $"Patch {installed.Patch} is installed; the application restarted." : saved.LastResult };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            // Never restore an older enabled preference over a damaged opt-out file.
            _state = new(installed.Label, Message: "Patch settings could not be read; automatic installation is off. Save the preference to recover.");
        }
    }

    public PatchUpdateStatus Status { get { lock (_stateGate) return _state; } }
    private bool IsPatch(string version)
    {
        var candidate = UpdateRelease.Parse(version);
        return candidate.Patch > 0 && UpdateRelease.CanInstall(_installed, candidate, _installed.Channel);
    }
    private void Save(PatchUpdateStatus state)
    {
        DurableJson.Write(_path, state, backup: false);
        _state = state;
    }
    public PatchUpdateStatus SetAutoInstall(bool enabled)
    {
        lock (_stateGate)
        {
            Save(_state with { AutoInstall = enabled, Message = enabled
                ? "Automatic patches will install during the next enabled Live View restart schedule. The whole application restarts when a patch is installed."
                : "Automatic patch installation is off. Checks continue; patches can be installed manually. An installation already started cannot be canceled." });
            return _state;
        }
    }

    public async Task<PatchUpdateStatus> CheckAsync(bool manual, DateTimeOffset now, CancellationToken token = default)
    {
        await _checkGate.WaitAsync(token);
        try
        {
            lock (_stateGate)
            {
                if (manual ? _state.LastChecked > now.AddMinutes(-2) : _state.NextCheck > now) return _state;
                Save(_state with { LastChecked = now, NextCheck = now.AddHours(24) });
            }
            string? candidate;
            try { candidate = await _check(token); if (candidate is not null && !IsPatch(candidate)) throw new InvalidDataException(); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                lock (_stateGate)
                {
                    var retry = error is GitHubRateLimitException limited && limited.RetryAt > now.AddHours(1) ? limited.RetryAt : now.AddHours(1);
                    Save(_state with { AvailableVersion = null, NextCheck = retry, Message = "Patch check unavailable; it will retry automatically. Nothing was installed." });
                    return _state;
                }
            }
            lock (_stateGate)
            {
                Save(_state with { AvailableVersion = candidate, Message = candidate is null ? "No newer patch is available for this release and channel."
                    : "A patch is available. Automatic installation waits for an enabled Live View restart schedule; you can also install it manually." });
                return _state;
            }
        }
        finally { _checkGate.Release(); }
    }

    // The scheduler owns its maintenance gate and has already consumed this occurrence.
    public async Task<UpdateLaunchResult?> InstallScheduledAsync(CancellationToken token)
    {
        var selected = Status;
        if (!selected.AutoInstall || selected.AvailableVersion is not { } version) return null;
        bool StillEnabled() { lock (_stateGate) return _state.AutoInstall && _state.AvailableVersion == version; }
        lock (_stateGate) Save(_state with { LastAttempt = DateTimeOffset.UtcNow, LastResult = "Scheduled patch preparation started; completion not yet confirmed.", Message = "Preparing the patch for this scheduled restart." });
        UpdateLaunchResult result;
        try { result = await _install(version, StillEnabled, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { result = new(false, "The scheduled patch could not be installed. Check the maintenance helper in Settings. It will retry at a later scheduled restart."); }
        lock (_stateGate) Save(_state with { Message = result.Message, LastResult = result.Message });
        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckAsync(false, DateTimeOffset.UtcNow, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { /* Persistence errors must never start an installation. */ }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
