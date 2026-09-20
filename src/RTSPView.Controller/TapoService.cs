using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

public sealed record TapoRequest(TapoSettings Settings, string? Password = null, bool ClearPassword = false, string? Revision = null);
public sealed record TapoTestRequest(ContactState State);
public sealed record TapoConnection(string Username, string Password, TapoHub[] Hubs, bool DiscoverHubs = false);
internal sealed record StoredTapo(TapoSettings Settings, string ProtectedPassword);
public sealed record TapoStatus(string Connection, string Message, DateTimeOffset? LastChecked, TapoHubStatus[] Hubs, TapoSensor[] Sensors, string[] ActiveRules, string[] TestingRules)
{
    public AutomationDeliveryStatus? Delivery { get; init; }
    public string? ConfigurationError { get; init; }
    public AutomationActivityEntry[] Activity { get; init; } = [];
    public IReadOnlyDictionary<string, DateTimeOffset?> LastTriggered { get; init; } = new Dictionary<string, DateTimeOffset?>();
    public string? ActivityError { get; init; }
    public IReadOnlyDictionary<string, string> RuleErrors { get; init; } = new Dictionary<string, string>();
}

public interface ITapoReader : IDisposable
{
    Task<TapoSnapshot> ReadAsync(TapoConnection connection, CancellationToken token);
}

// The independently packaged GPL helper owns protocol/authentication handling.
// Credentials travel over private standard input, never arguments or logs.
public sealed class TapoProcessReader : ITapoReader
{
    private Process? _process;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<TapoSnapshot> ReadAsync(TapoConnection connection, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            if (_process is null || _process.HasExited)
            {
                Stop();
                var executable = Path.Combine(AppContext.BaseDirectory, "Tapo", "tapo-reader.exe");
                if (!File.Exists(executable)) throw new InvalidDataException("Tapo support is missing from this installation. Install the complete beta package.");
                _process = Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                }) ?? throw new IOException("Tapo helper could not start.");
                // Discard diagnostics. Third-party errors can contain device/account data.
                _process.ErrorDataReceived += (_, _) => { };
                _process.BeginErrorReadLine();
            }
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(connection, Json).AsMemory(), timeout.Token);
            await _process.StandardInput.FlushAsync(timeout.Token);
            var line = await _process.StandardOutput.ReadLineAsync(timeout.Token);
            if (line is null || line.Length > 262144) throw new IOException("Invalid Tapo response.");
            var result = JsonSerializer.Deserialize<TapoSnapshot>(line, Json) ?? throw new IOException("Missing Tapo response.");
            if (result.Hubs is null || result.Sensors is null || result.Sensors.Length > 512 ||
                (result.DiscoveredHubs is not null && (result.DiscoveredHubs.Length > 64 || result.DiscoveredHubs.Any(h => h is null || !Guid.TryParse(h.Id, out _) || string.IsNullOrWhiteSpace(h.Name) || h.Name.Length > 100 || !System.Net.IPAddress.TryParse(h.Host, out _)))) ||
                result.Sensors.Any(s => s is null || !Enum.IsDefined(s.State) || !connection.Hubs.Any(h => h.Id == s.HubId)) ||
                result.Sensors.Select(s => (s.HubId, s.DeviceId)).Distinct().Count() != result.Sensors.Length)
                throw new IOException("Invalid Tapo sensor inventory.");
            return result;
        }
        catch { Stop(); throw; }
        finally { _gate.Release(); }
    }
    public async Task ResetAsync()
    {
        await _gate.WaitAsync();
        try { Stop(); } finally { _gate.Release(); }
    }
    private void Stop()
    {
        if (_process is null) return;
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        _process.Dispose(); _process = null;
    }
    public void Dispose() { Stop(); _gate.Dispose(); }
}

public sealed class TapoService : BackgroundService
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _save = new(1, 1), _send = new(1, 1);
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly JsonSettingsStore _app;
    private readonly Func<TapoConnection, CancellationToken, Task<TapoSnapshot>> _read;
    private readonly ITapoReader? _reader;
    private readonly Func<ViewerCommand, CancellationToken, Task<ViewerCommandResult>> _viewer;
    private StoredTapo _stored = new(new(), "");
    private TapoStatus _status = new("Disabled", "Add your hubs to discover T110 door sensors.", null, [], [], [], []);
    private readonly Dictionary<string, (ContactState State, DateTimeOffset Until)> _tests = new();
    private CancellationTokenSource _changed = new();
    private bool _hasPresentedEffects;
    private string? _loadError;
    public AutomationActivity Activity { get; }
    private readonly SemaphoreSlim _discovery = new(1, 1);
    public TapoSettings CurrentSettings { get { lock (_sync) return _stored.Settings; } }
    internal StoredTapo StoredState { get { lock (_sync) return _stored; } }
    internal async Task RestoreStateAsync(StoredTapo stored)
    {
        await _save.WaitAsync();
        try { await PersistAsync(stored, CancellationToken.None); }
        finally { _save.Release(); }
    }
    public bool UsesCamera(int slot) { lock (_sync) return _stored.Settings.Rules.Any(r => (r.Action == SensorAction.AutomationLayout || r.ClearAction == SensorAction.AutomationLayout) && (r.FocusCameraSlot == slot || r.SecondFocusCameraSlot == slot)); }
    public bool UsesAutomationLayout(string id) { lock (_sync) return _stored.Settings.Rules.Any(r => (r.Action == SensorAction.AutomationLayout && r.LayoutId == id) || (r.ClearAction == SensorAction.AutomationLayout && r.ClearLayoutId == id)); }
    public bool RequiresSecondFocus(string id) { lock (_sync) return _stored.Settings.Rules.Any(r => r.SecondFocusCameraSlot != 0 && ((r.Action == SensorAction.AutomationLayout && r.LayoutId == id) || (r.ClearAction == SensorAction.AutomationLayout && r.ClearLayoutId == id))); }
    public async Task<TapoSnapshot> DiscoverHubsAsync(CancellationToken token)
    {
        if (!await _discovery.WaitAsync(0, token)) throw new InvalidDataException("Hub discovery is already running.");
        try
        {
            using var reader = new TapoProcessReader();
            var result = await reader.ReadAsync(new("", "", [], true), token);
            if (result.DiscoveredHubs is null) throw new IOException("Hub discovery did not return a result.");
            return result;
        }
        finally { _discovery.Release(); }
    }

    public TapoService(string directory, IDataProtectionProvider protection, ViewerCommandClient viewer)
        : this(directory, protection, viewer.SendAsync, new TapoProcessReader()) { }
    public TapoService(string directory, IDataProtectionProvider protection,
        Func<ViewerCommand, CancellationToken, Task<ViewerCommandResult>> viewer, ITapoReader reader)
    {
        _path = Path.Combine(directory, "tapo.json");
        Activity = new(Path.Combine(directory, "tapo-activity.json"));
        _app = new(Path.Combine(directory, "settings.json"));
        _protector = protection.CreateProtector("RTSPView.Tapo.Password.v1");
        _viewer = viewer; _reader = reader; _read = reader.ReadAsync;
        try
        {
            _stored = AutomationPersistence.Load(_path, _stored, value => {
                if (value.Settings is null || value.ProtectedPassword is null) throw new InvalidDataException();
                value.Settings.Validate(new AppSettings().Normalize(), validateTargets: false);
            }, out var recovered);
            if (recovered) Activity.Add("", "Tapo", "Recovery", "Recovered settings from the latest valid local backup.");
        }
        catch { _stored = new(new(), ""); _loadError = "Tapo settings could not be loaded. Restore a backup or save valid settings to recover."; _status = _status with { Connection = "Error", Message = _loadError, ConfigurationError = _loadError }; }
    }
    public object Configuration { get { lock (_sync) return new { settings = _stored.Settings, hasPassword = _stored.ProtectedPassword.Length > 0, revision = AutomationRevision.For(_stored.Settings) }; } }
    public TapoStatus Status
    {
        get
        {
            lock (_sync)
            {
                var now = DateTimeOffset.UtcNow;
                var stale = _status.LastChecked is null || now - _status.LastChecked > TimeSpan.FromSeconds(_stored.Settings.PollSeconds + 30);
                return _status with { Sensors = stale ? _status.Sensors.Select(s => s with { State = ContactState.Unavailable }).ToArray() : _status.Sensors,
                    TestingRules = _tests.Where(t => t.Value.Until > now).Select(t => t.Key).ToArray(),
                    Activity = Activity.Entries, ActivityError = Activity.Error,
                    LastTriggered = _stored.Settings.Rules.ToDictionary(r => r.Id, r => Activity.LastTrigger(r.Id)) };
            }
        }
    }
    public bool UsesOverlay(int slot) { lock (_sync) return _stored.Settings.Rules.Any(r => r.OverlaySlot == slot && new[] { r.Action, r.ClearAction, r.UnavailableAction }.Any(a => a is SensorAction.ShowOverlay or SensorAction.HideOverlay)); }
    public bool UsesLayout(string id) { lock (_sync) return _stored.Settings.Rules.Any(r => (r.Action == SensorAction.Layout && r.LayoutId == id) || (r.ClearAction == SensorAction.Layout && r.ClearLayoutId == id)); }
    private string Password(TapoRequest request)
    {
        if (request.ClearPassword) return "";
        if (!string.IsNullOrEmpty(request.Password)) return request.Password;
        return _stored.ProtectedPassword.Length > 0 ? _protector.Unprotect(_stored.ProtectedPassword) : "";
    }
    public async Task SaveAsync(TapoRequest request, CancellationToken token)
    {
        await _save.WaitAsync(token);
        try
        {
            if (request.Settings is null) throw new InvalidDataException("Missing Tapo settings.");
            AutomationRevision.Check(request.Revision, AutomationRevision.For(CurrentSettings));
            request.Settings.Validate(await _app.LoadAsync(token), validateTargets: request.Settings.Enabled);
            string password; lock (_sync) password = Password(request);
            if (password.Length > 1024 || (request.Settings.Enabled && password.Length == 0)) throw new InvalidDataException("Enter your Tapo password.");
            var stored = new StoredTapo(request.Settings, password.Length == 0 ? "" : _protector.Protect(password));
            await PersistAsync(stored, token, request.Password is null && !request.ClearPassword);
        }
        finally { _save.Release(); }
    }
    private async Task PersistAsync(StoredTapo stored, CancellationToken token, bool preserveRuntime = false)
    {
        await _send.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            AutomationPersistence.Save(_path, JsonSerializer.Serialize(stored), _loadError is not null);
            lock (_sync)
            {
                var priorityOnly = preserveRuntime && _loadError is null && JsonSerializer.Serialize(_stored.Settings with { Rules = _stored.Settings.Rules.Select(r => r with { Priority = 50 }).ToArray() }) ==
                    JsonSerializer.Serialize(stored.Settings with { Rules = stored.Settings.Rules.Select(r => r with { Priority = 50 }).ToArray() });
                _loadError = null;
                if (priorityOnly) { _stored = stored; return; }
                _stored = stored; _tests.Clear(); _changed.Cancel(); _changed.Dispose(); _changed = new();
                _status = new(stored.Settings.Enabled ? "Connecting" : "Disabled", "Settings saved.", null, [], [], [], []);
            }
        }
        finally { _send.Release(); }
    }
    public async Task RemoveAccountAsync(CancellationToken token)
    {
        await _save.WaitAsync(token);
        try
        {
            StoredTapo stored;
            lock (_sync) stored = new(_stored.Settings with { Enabled = false, Username = "" }, "");
            // Removing credentials must work even if an old rule target no longer exists.
            await PersistAsync(stored, token);
            if (_reader is TapoProcessReader process) await process.ResetAsync();
        }
        finally { _save.Release(); }
        await PresentAsync(CancellationToken.None);
    }
    public async Task<TapoSnapshot> DiscoverAsync(TapoRequest request, CancellationToken token)
    {
        if (request.Settings is null) throw new InvalidDataException("Missing Tapo settings.");
        // Discover draft hubs without saving settings or firing any rule.
        (request.Settings with { Rules = [] }).Validate(await _app.LoadAsync(token), connect: true);
        string password; lock (_sync) password = Password(request);
        if (password.Length is < 1 or > 1024) throw new InvalidDataException("Enter your Tapo password.");
        using var reader = new TapoProcessReader();
        return await reader.ReadAsync(new(request.Settings.Username, password, request.Settings.Hubs), token);
    }
    public async Task<ViewerCommandResult> TestAsync(string id, ContactState state, CancellationToken token)
    {
        if (!Enum.IsDefined(state)) throw new InvalidDataException("Select Open, Closed, or Unavailable.");
        var app = await _app.LoadAsync(token);
        lock (_sync)
        {
            if (!_stored.Settings.Enabled || !_stored.Settings.Rules.Any(r => r.Id == id && r.Enabled))
                throw new InvalidDataException("Save and enable this sensor rule before testing.");
            (_stored.Settings with { Rules = [_stored.Settings.Rules.Single(r => r.Id == id)] }).Validate(app);
            _tests[id] = (state, DateTimeOffset.UtcNow.AddSeconds(10));
        }
        return await PresentAsync(token);
    }
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(PollAsync(stoppingToken), PresentLoopAsync(stoppingToken));
    private async Task PollAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            StoredTapo stored; CancellationToken changed;
            lock (_sync) { stored = _stored; changed = _changed.Token; }
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(token, changed);
            try
            {
                if (stored.Settings.Enabled)
                {
                    (stored.Settings with { Rules = [] }).Validate(await _app.LoadAsync(scope.Token));
                    var snapshot = await _read(new(stored.Settings.Username, _protector.Unprotect(stored.ProtectedPassword), stored.Settings.Hubs), scope.Token);
                    lock (_sync)
                    {
                        if (ReferenceEquals(stored, _stored))
                        {
                            var connected = snapshot.Hubs.Count(h => h.Connected);
                            // Preserve missing sensors as unavailable, so a lost hub never means Closed.
                            var missing = _status.Sensors.Where(s => !snapshot.Sensors.Any(n => n.HubId == s.HubId && n.DeviceId == s.DeviceId))
                                .Select(s => s with { State = ContactState.Unavailable });
                            _status = _status with { Connection = connected == stored.Settings.Hubs.Length ? "Connected" : connected > 0 ? "Partial" : "Unavailable",
                                Message = connected > 0 ? "Reading sensors locally through Tapo hubs." : "Check hub addresses, credentials and Third-Party Compatibility in Tapo.",
                                LastChecked = DateTimeOffset.UtcNow, Hubs = snapshot.Hubs, Sensors = snapshot.Sensors.Concat(missing).ToArray() };
                        }
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(stored.Settings.Enabled ? stored.Settings.PollSeconds : 1), scope.Token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested || changed.IsCancellationRequested) { }
            catch (Exception e)
            {
                lock (_sync) if (ReferenceEquals(stored, _stored)) _status = _status with { Connection = "Unavailable",
                    Message = e is InvalidDataException ? e.Message : "Tapo connection failed. Check hub addresses, credentials and Third-Party Compatibility.",
                    LastChecked = DateTimeOffset.UtcNow, Hubs = [], Sensors = _status.Sensors.Select(s => s with { State = ContactState.Unavailable }).ToArray() };
                try { await Task.Delay(TimeSpan.FromSeconds(stored.Settings.PollSeconds), scope.Token); } catch (OperationCanceledException) { }
            }
        }
    }
    private async Task PresentLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await PresentAsync(token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch { lock (_sync) _status = _status with { Delivery = new(DateTimeOffset.UtcNow, false, "Viewer delivery failed. Check Live View and the saved configuration.") }; }
            try { await Task.Delay(1000, token); } catch (OperationCanceledException) { break; }
        }
    }
    private async Task<ViewerCommandResult> PresentAsync(CancellationToken token)
    {
        await _send.WaitAsync(token);
        try
        {
            var app = await _app.LoadAsync(token);
            SensorEffect[] effects;
            lock (_sync)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var id in _tests.Where(t => t.Value.Until <= now).Select(t => t.Key).ToArray()) _tests.Remove(id);
                var sensorReadings = Status.Sensors;
                var invalid = new Dictionary<string, string>();
                try
                {
                    (_stored.Settings with { Rules = [] }).Validate(app);
                    foreach (var rule in _stored.Settings.Rules.Where(r => r.Enabled && _stored.Settings.Enabled))
                        try { (_stored.Settings with { Rules = [rule] }).Validate(app); }
                        catch (InvalidDataException e) { invalid[rule.Id] = e.Message; Activity.Add(rule.Id, rule.Name, "Configuration", "Invalid target; rule skipped", true); }
                    effects = _stored.Settings.Rules.Where(r => !invalid.ContainsKey(r.Id)).SelectMany(rule =>
                        TapoRuleEngine.Evaluate(_stored.Settings with { Rules = [rule] }, _tests.TryGetValue(rule.Id, out var test)
                            ? [new TapoSensor(rule.HubId, rule.DeviceId, rule.Name, "T110", test.State)] : sensorReadings)).ToArray();
                }
                catch (InvalidDataException e) { effects = []; invalid["integration"] = e.Message; }
                foreach (var rule in _stored.Settings.Rules)
                {
                    var state = _tests.TryGetValue(rule.Id, out var simulation) ? simulation.State : sensorReadings.FirstOrDefault(s => s.HubId == rule.HubId && s.DeviceId == rule.DeviceId)?.State ?? ContactState.Unavailable;
                    Activity.Add(rule.Id, rule.Name, "Sensor", !_stored.Settings.Enabled || !rule.Enabled ? "Rule disabled" : (_tests.ContainsKey(rule.Id) ? "Simulated " : "") + state, true);
                    var effect = effects.FirstOrDefault(e => e.RuleId == rule.Id);
                    Activity.Add(rule.Id, rule.Name, effect is null ? "State" : "Trigger", effect is null ? "No override requested" : (_tests.ContainsKey(rule.Id) ? "Simulated: " : "Sensor: ") + effect.Action, true);
                    if (effect is null) { Activity.ResetTransition(rule.Id, "Trigger"); Activity.ResetTransition(rule.Id, "Presentation"); Activity.ResetTransition(rule.Id, "Delivery"); }
                    else Activity.ResetTransition(rule.Id, "State");
                }
                _status = _status with { ActiveRules = effects.Select(e => e.RuleId).ToArray(), RuleErrors = invalid,
                    ConfigurationError = _loadError ?? (invalid.Count > 0 ? "Some rules need attention; valid rules remain enabled." : null) };
            }
            if (effects.Length == 0 && !_hasPresentedEffects)
                return new(Guid.Empty, true, "No sensor override active.");
            // Short leases ensure crashes and Controller outages restore the saved wall.
            var presentation = new SensorPresentation(AutomationConfiguration.Hash(app), DateTimeOffset.UtcNow.AddSeconds(5), effects);
            var result = await _viewer(new(Guid.NewGuid(), ViewerCommandType.SensorAutomation, Sensors: presentation), token);
            foreach (var effect in effects) Activity.Add(effect.RuleId, CurrentSettings.Rules.FirstOrDefault(r => r.Id == effect.RuleId)?.Name ?? "Removed rule", "Delivery", result.Success ? "Viewer acknowledged" : "Viewer delivery failed", true);
            lock (_sync)
            {
                _status = _status with { Delivery = new(DateTimeOffset.UtcNow, result.Success, result.Message) };
                if (result.Success) _hasPresentedEffects = effects.Length > 0;
            }
            return result;
        }
        finally { _send.Release(); }
    }
    public override void Dispose() { _reader?.Dispose(); _changed.Dispose(); base.Dispose(); }
}
