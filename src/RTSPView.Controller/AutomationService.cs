using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.DataProtection;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

public sealed record AutomationRequest(AutomationSettings Settings, string? Password, bool ClearPassword = false, string? Revision = null);
public sealed record MqttDiagnosticsRequest(AutomationRequest Connection, string Prefix = "scrypted");
public sealed record AutomationRuleTestRequest(int SourceSlot);
internal sealed record PendingRuleTest(string RuleId, int SourceSlot, int Revision, CancellationToken Token, TaskCompletionSource<ViewerCommandResult> Completion);
internal sealed record StoredAutomation(AutomationSettings Settings, string ProtectedPassword);
public sealed record AutomationStatus(string Connection, string LastResult, DateTimeOffset? LastMessage,
    DateTimeOffset? LastPerson, object[] Rules)
{
    public AutomationDeliveryStatus? Delivery { get; init; }
    public AutomationDeliveryStatus? ViewerConnection { get; init; }
    public string? ConfigurationError { get; init; }
    public long DroppedEvents { get; init; }
    public AutomationActivityEntry[] Activity { get; init; } = [];
    public string? ActivityError { get; init; }
}

public sealed class AutomationService : BackgroundService
{
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly ViewerCommandClient _viewer;
    private readonly JsonSettingsStore _cameras;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private StoredAutomation _stored = new(new(), "");
    private int _revision;
    private int _credentialRevision;
    private string? _loadError;
    private readonly Dictionary<string, AutomationDeliveryStatus> _ruleDelivery = new();
    private HashSet<string> _requestedRules = [];
    private readonly Channel<PendingRuleTest> _ruleTests = Channel.CreateBounded<PendingRuleTest>(32);
    private AutomationStatus _status = new("Disabled", "Waiting", null, null, []);
    private long _droppedEvents;
    public AutomationActivity Activity { get; }
    internal string DirectoryPath => Path.GetDirectoryName(_path)!;
    public AutomationStatus Status => _status with { DroppedEvents = Interlocked.Read(ref _droppedEvents), Activity = Activity.Entries, ActivityError = Activity.Error };
    public MqttDiagnostics Diagnostics { get; } = new();
    public object Configuration => new { settings = _stored.Settings, hasPassword = _stored.ProtectedPassword.Length > 0, revision = AutomationRevision.For(_stored.Settings) };
    public AutomationSettings CurrentSettings => _stored.Settings;
    internal StoredAutomation StoredState => _stored;
    internal async Task RestoreStateAsync(StoredAutomation stored)
    {
        await _saveGate.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(_path + ".tmp", JsonSerializer.Serialize(stored));
            File.Move(_path + ".tmp", _path, true);
            _stored = stored; _loadError = null;
            Interlocked.Increment(ref _credentialRevision); Interlocked.Increment(ref _revision);
        }
        finally { _saveGate.Release(); }
    }
    public bool UsesCamera(int slot) => _stored.Settings.Rules.Any(r => r.Sources.Any(s => s.CameraSlot == slot) || (r.Action != AutomationAction.Overlay && (r.CameraSlot == slot || r.SecondCameraSlot == slot)));
    public bool UsesOverlay(int slot) => UsesCamera(slot) || UsesCamera(StreamCatalog.SourceSlot(slot)) || _stored.Settings.Rules.Any(r => r.Action == AutomationAction.Overlay && r.OverlaySlot == slot);
    public bool RequiresSecondFocus(string id) => _stored.Settings.Rules.Any(r => r.LayoutId == id && r.SecondCameraSlot != 0);
    public bool UsesLayout(string id) => _stored.Settings.Rules.Any(r => r.Action == AutomationAction.FocusedLayout && r.LayoutId == id);

    public AutomationService(string directory, IDataProtectionProvider protection, ViewerCommandClient viewer)
    {
        _path = Path.Combine(directory, "automation.json");
        Activity = new(Path.Combine(directory, "mqtt-activity.json"));
        _protector = protection.CreateProtector("RTSPView.Mqtt.Password.v1");
        _viewer = viewer;
        _cameras = new(Path.Combine(directory, "settings.json"));
        try
        {
            _stored = AutomationPersistence.Load(_path, _stored, value => {
                if (value.Settings is null || value.ProtectedPassword is null) throw new InvalidDataException();
                value.Settings.Validate(new AppSettings().Normalize(), validateTargets: false);
            }, out var recovered);
            if (recovered) Activity.Add("", "MQTT", "Recovery", "Recovered settings from the latest valid local backup.");
        }
        catch { _stored = new(new(), ""); _loadError = "Automation settings could not be loaded. Restore a backup or save valid settings to recover."; _status = new("Error", _loadError, null, null, []) { ConfigurationError = _loadError }; }
    }

    private string Password(AutomationRequest request)
    {
        if (request.ClearPassword) return "";
        if (!string.IsNullOrEmpty(request.Password)) return request.Password;
        return _stored.ProtectedPassword.Length == 0 ? "" : _protector.Unprotect(_stored.ProtectedPassword);
    }

    public async Task SaveAsync(AutomationRequest request, CancellationToken token)
    {
        await _saveGate.WaitAsync(token);
        try
        {
            AutomationRevision.Check(request.Revision, AutomationRevision.For(_stored.Settings));
            request.Settings.Validate(await _cameras.LoadAsync(token), validateTargets: request.Settings.Enabled);
            var password = Password(request);
            if (password.Length > 1024) throw new InvalidDataException("Password is too long.");
            var stored = new StoredAutomation(request.Settings, password.Length == 0 ? "" : _protector.Protect(password));
            token.ThrowIfCancellationRequested();
            AutomationPersistence.Save(_path, JsonSerializer.Serialize(stored), _loadError is not null);
            _stored = stored;
            _loadError = null;
            if (request.ClearPassword || !string.IsNullOrEmpty(request.Password)) Interlocked.Increment(ref _credentialRevision);
            Interlocked.Increment(ref _revision);
        }
        finally { _saveGate.Release(); }
    }

    public async Task<ViewerCommandResult> TestRuleAsync(string ruleId, int sourceSlot, CancellationToken token)
    {
        var settings = _stored.Settings.ResolveSources();
        var rule = settings.Rules.SingleOrDefault(r => r.Id == ruleId);
        if (!settings.Enabled || rule?.Enabled != true) throw new InvalidDataException("Enable automation and save an enabled rule before testing.");
        if (!rule.Sources.Any(s => s.CameraSlot == sourceSlot)) throw new InvalidDataException("Select a source camera from this saved rule.");
        var completion = new TaskCompletionSource<ViewerCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        if (!_ruleTests.Writer.TryWrite(new(ruleId, sourceSlot, _revision, timeout.Token, completion)))
            throw new InvalidDataException("Another test is pending. Try again shortly.");
        return await completion.Task.WaitAsync(timeout.Token);
    }

    private static MqttClientOptions Options(AutomationSettings settings, string password, bool test = false)
    {
        var builder = new MqttClientOptionsBuilder().WithTcpServer(settings.Host, settings.Port)
            .WithClientId(test ? "rtspview-test-" + Guid.NewGuid().ToString("N")[..12] : settings.ClientId)
            .WithProtocolVersion(MqttProtocolVersion.V311).WithCleanSession().WithTimeout(TimeSpan.FromSeconds(8));
        if (settings.Authenticate) builder.WithCredentials(settings.Username, password);
        if (settings.Tls) builder.WithTlsOptions(o => o.UseTls()); // OS trust store and hostname validation; no bypass.
        return builder.Build();
    }

    private static async Task Subscribe(IMqttClient client, AutomationSettings settings, CancellationToken token)
    {
        var topics = settings.ResolveSources().Rules.Where(r => r.Enabled).SelectMany(r => r.Sources).Select(s => s.Topic).Distinct().ToArray();
        if (topics.Length == 0) return;
        var builder = new MqttClientSubscribeOptionsBuilder();
        foreach (var topic in topics) builder.WithTopicFilter(topic);
        var result = await client.SubscribeAsync(builder.Build(), token);
        if (result.Items.Any(i => (int)i.ResultCode >= 128)) throw new InvalidDataException("Broker rejected a subscription.");
    }

    public async Task<object> TestAsync(AutomationRequest request, CancellationToken token)
    {
        request.Settings.Validate(await _cameras.LoadAsync(token), true, validateTargets: false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var client = new MqttFactory().CreateMqttClient();
        try
        {
            await client.ConnectAsync(Options(request.Settings, Password(request), true), timeout.Token);
            await Subscribe(client, request.Settings, timeout.Token);
            await client.DisconnectAsync(new MqttClientDisconnectOptions(), timeout.Token);
            return new { success = true, message = "Connected. Broker accepted the connection" +
                (request.Settings.Rules.Any(r => r.Enabled) ? " and enabled rule subscriptions" : "") + ". No actions were enabled or settings saved." };
        }
        catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested)
        { return new { success = false, message = "Connection or subscription failed. Check host, port, authentication, TLS trust and broker permissions." }; }
    }

    public async Task StartDiagnosticsAsync(MqttDiagnosticsRequest request, CancellationToken token)
    {
        if (request.Connection?.Settings is null) throw new InvalidDataException("Connection settings are required.");
        var settings = request.Connection.Settings with { Enabled = false, Rules = [] };
        settings.Validate(await _cameras.LoadAsync(token), true);
        await Diagnostics.StartAsync(Options(settings, Password(request.Connection), true), request.Prefix, token);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await Diagnostics.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose() { Diagnostics.Dispose(); base.Dispose(); }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var engine = new PersonOverlayEngine();
        var channel = Channel.CreateBounded<(string Topic, byte[] Payload, bool Retain, DateTimeOffset Received)>(new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.DropOldest }, _ => Interlocked.Increment(ref _droppedEvents));
        IMqttClient? client = null;
        var revision = -1;
        var hash = "";
        var sent = "";
        var lastEvents = new Dictionary<string, DateTimeOffset>();
        var lastTriggered = new Dictionary<string, DateTimeOffset>();
        var sentRules = new Dictionary<string, string>();
        var connectedAt = DateTimeOffset.MinValue;
        var nextConnect = DateTimeOffset.MinValue;
        var failures = 0;
        AutomationSettings? applied = null;
        var appliedCredentials = -1;
        long reportedDrops = 0;
        var nextViewerProbe = DateTimeOffset.MinValue;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                var drops = Interlocked.Read(ref _droppedEvents);
                if (drops != reportedDrops) { reportedDrops = drops; Activity.Add("", "MQTT", "Queue", $"Dropped events since Controller start: {drops}"); }
                try
                {
                    // Probe connectivity without replaying a timed-out automation command.
                    // Preserve Delivery and per-rule history as the outcome of that action.
                    if (_stored.Settings.Enabled && _status.Delivery?.Success == false && now >= nextViewerProbe)
                    {
                        var probe = await _viewer.SendAsync(ViewerCommandType.Ping, null, stoppingToken);
                        _status = _status with { ViewerConnection = new(DateTimeOffset.UtcNow, probe.Success, probe.Message) };
                        nextViewerProbe = DateTimeOffset.UtcNow.AddSeconds(5);
                    }
                    var cameras = await _cameras.LoadAsync(stoppingToken);
                    var currentHash = AutomationConfiguration.Hash(cameras);
                    if (revision != _revision || hash != currentHash)
                    {
                        var updated = _stored.Settings;
                        var priorityOnly = hash == currentHash && appliedCredentials == _credentialRevision && applied is not null &&
                            JsonSerializer.Serialize(applied with { Rules = applied.Rules.Select(r => r with { Priority = 50 }).ToArray() }) ==
                            JsonSerializer.Serialize(updated with { Rules = updated.Rules.Select(r => r with { Priority = 50 }).ToArray() });
                        revision = _revision; hash = currentHash;
                        applied = updated;
                        appliedCredentials = _credentialRevision;
                        if (priorityOnly) engine.RetainRules(updated.Rules);
                        else
                        {
                            client?.Dispose(); client = null;
                            while (channel.Reader.TryRead(out _)) { }
                            engine.Clear(); sent = ""; lastEvents.Clear(); lastTriggered.Clear(); sentRules.Clear(); _ruleDelivery.Clear(); nextConnect = now; failures = 0;
                            await Send(engine, hash, stoppingToken);
                        }
                    }
                    if (_loadError is not null)
                    {
                        _status = _status with { Connection = "Error", LastResult = _loadError, ConfigurationError = _loadError, Rules = [] };
                        await Task.Delay(500, stoppingToken);
                        continue;
                    }
                    var configured = _stored.Settings.ResolveSources();
                    (configured with { Rules = [] }).Validate(cameras);
                    var invalid = new Dictionary<string, string>();
                    foreach (var rule in configured.Rules.Where(r => r.Enabled && configured.Enabled))
                        try { (configured with { Rules = [rule] }).Validate(cameras); }
                        catch (InvalidDataException e) { invalid[rule.Id] = e.Message; Activity.Add(rule.Id, rule.Name, "Configuration", "Invalid target; rule skipped", true); }
                    var settings = configured with { Rules = configured.Rules.Where(r => !invalid.ContainsKey(r.Id)).ToArray() };
                    engine.RetainRules(settings.Rules);
                    _status = _status with { ConfigurationError = invalid.Count > 0 ? "Some rules need attention; valid rules remain enabled." : null };
                    while (_ruleTests.Reader.TryRead(out var test))
                    {
                        if (test.Token.IsCancellationRequested) continue;
                        var rule = settings.Rules.SingleOrDefault(r => r.Id == test.RuleId);
                        if (test.Revision != revision || !settings.Enabled || rule?.Enabled != true)
                        {
                            test.Completion.TrySetException(new InvalidDataException("Settings changed. Apply changes and test again."));
                            continue;
                        }
                        try
                        {
                            engine.Trigger(rule, test.SourceSlot, DateTimeOffset.UtcNow);
                            Activity.Add(rule.Id, rule.Name, "Trigger", "Simulated person detection");
                            var result = await Send(engine, hash, stoppingToken);
                            sent = JsonSerializer.Serialize(engine.Leases.Values);
                            if (result.Success) lastTriggered[rule.Id] = DateTimeOffset.UtcNow;
                            test.Completion.TrySetResult(result);
                        }
                        catch (Exception e) { test.Completion.TrySetException(e); }
                    }
                    if (!settings.Enabled)
                    {
                        _status = _status with { Connection = "Disabled", LastResult = "Automation disabled", Rules = [] };
                        await Task.Delay(500, stoppingToken);
                        continue;
                    }
                    if (client?.IsConnected != true && now >= nextConnect)
                    {
                        client?.Dispose();
                        while (channel.Reader.TryRead(out _)) { }
                        client = new MqttFactory().CreateMqttClient();
                        client.ApplicationMessageReceivedAsync += e =>
                        {
                            var m = e.ApplicationMessage;
                            if (m.PayloadSegment.Count <= 65536) channel.Writer.TryWrite((m.Topic, m.PayloadSegment.ToArray(), m.Retain, DateTimeOffset.UtcNow));
                            return Task.CompletedTask;
                        };
                        _status = _status with { Connection = failures == 0 ? "Connecting" : "Reconnecting" };
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        timeout.CancelAfter(TimeSpan.FromSeconds(10));
                        connectedAt = DateTimeOffset.UtcNow;
                        await client.ConnectAsync(Options(settings, _stored.ProtectedPassword.Length == 0 ? "" : _protector.Unprotect(_stored.ProtectedPassword)), timeout.Token);
                        await Subscribe(client, settings, timeout.Token);
                        failures = 0;
                        _status = _status with { Connection = "Connected", LastResult = "Subscribed; waiting for person detections" };
                    }
                    if (revision != _revision) continue;
                    if (client?.IsConnected != true) _status = _status with { Connection = "Reconnecting" };
                    // Drain bounded input; only fresh events can renew deadlines.
                    for (var count = 0; count < 128 && channel.Reader.TryRead(out var message); count++)
                    {
                        foreach (var rule in settings.Rules.Where(r => r.Enabled && r.Sources.Any(source => source.Topic == message.Topic))) lastEvents[rule.Id] = message.Received;
                        var result = engine.Accept(settings, message.Topic, message.Payload, message.Retain, DateTimeOffset.UtcNow, connectedAt);
                        foreach (var rule in settings.Rules.Where(r => r.Enabled && r.Sources.Any(s => s.Topic == message.Topic)))
                        {
                            var decision = engine.LastDecisions.GetValueOrDefault(rule.Id, result);
                            Activity.Add(rule.Id, rule.Name, decision == "Person detected" ? "Trigger" : "Event", decision);
                        }
                        _status = _status with { LastMessage = message.Received, LastResult = result,
                            LastPerson = result == "Person detected" ? message.Received : _status.LastPerson };
                    }
                    engine.Expire(DateTimeOffset.UtcNow);
                    var signature = JsonSerializer.Serialize(engine.Leases.Values);
                    if (signature != sent)
                    {
                        sent = signature; // Do not replay a failed action without another fresh detection.
                        var result = await Send(engine, hash, stoppingToken);
                        if (!result.Success) _status = _status with { LastResult = result.Message };
                        else
                        {
                            foreach (var group in engine.Leases.Values.GroupBy(lease => lease.RuleId))
                            {
                                var ruleSignature = JsonSerializer.Serialize(group.ToArray());
                                if (!sentRules.TryGetValue(group.Key, out var prior) || prior != ruleSignature)
                                    lastTriggered[group.Key] = DateTimeOffset.UtcNow;
                                sentRules[group.Key] = ruleSignature;
                            }
                            foreach (var id in sentRules.Keys.Where(id => !engine.Leases.Values.Any(lease => lease.RuleId == id)).ToArray()) sentRules.Remove(id);
                        }
                    }
                    _status = _status with { Rules = configured.Rules.Select(r => (object)new { r.Id, r.Name, r.Enabled,
                        error = invalid.GetValueOrDefault(r.Id),
                        delivery = _ruleDelivery.GetValueOrDefault(r.Id),
                        lastEvent = lastEvents.TryGetValue(r.Id, out var received) ? (DateTimeOffset?)received : null,
                        lastTriggered = Activity.LastTrigger(r.Id),
                        expiresAt = engine.Leases.Values.Where(l => l.RuleId == r.Id).Select(l => (DateTimeOffset?)l.ExpiresAt).Max(),
                        activeCameraSlots = engine.Leases.Values.Where(l => l.RuleId == r.Id).Select(l => l.Slot).Distinct().ToArray() }).ToArray() };
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception e)
                {
                    client?.Dispose(); client = null;
                    nextConnect = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, Math.Min(++failures, 5))) + Random.Shared.NextDouble());
                    _status = _status with { Connection = "Error", LastResult = e is InvalidDataException ? e.Message : "MQTT unavailable. Check connection settings, credentials and TLS trust." };
                    engine.Expire(DateTimeOffset.UtcNow);
                }
                // Wake immediately for detections; retain the periodic timeout
                // for expiry, configuration changes and reconnect attempts.
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wake.CancelAfter(500);
                try { await channel.Reader.WaitToReadAsync(wake.Token); }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            }
        }
        finally { client?.Dispose(); }
    }

    private async Task<ViewerCommandResult> Send(PersonOverlayEngine engine, string hash, CancellationToken token)
    {
        var requested = engine.Leases.Values.Select(l => l.RuleId).ToHashSet();
        foreach (var id in _requestedRules.Except(requested))
        {
            Activity.Add(id, _stored.Settings.Rules.FirstOrDefault(r => r.Id == id)?.Name ?? "Removed rule", "State", "Override request expired or cleared");
            Activity.ResetTransition(id, "Delivery"); Activity.ResetTransition(id, "Presentation");
        }
        _requestedRules = requested;
        var result = await _viewer.SendAsync(new ViewerCommand(Guid.NewGuid(), ViewerCommandType.AutomationOverlays, Automation: new(hash, engine.Leases.Values.ToArray())), token);
        var delivery = new AutomationDeliveryStatus(DateTimeOffset.UtcNow, result.Success, result.Message);
        _status = _status with { Delivery = delivery, ViewerConnection = delivery };
        if (engine.Leases.Count == 0)
            Activity.Add("", "Viewer", "Delivery", result.Success ? "Viewer acknowledged" : result.Message, true);
        foreach (var id in engine.Leases.Values.Select(l => l.RuleId).Distinct())
        {
            _ruleDelivery[id] = _status.Delivery;
            Activity.Add(id, _stored.Settings.Rules.FirstOrDefault(r => r.Id == id)?.Name ?? "Removed rule", "Delivery", result.Success ? "Viewer acknowledged" : "Viewer delivery failed", true);
        }
        return result;
    }
}
