using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.DataProtection;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

public sealed record AutomationRequest(AutomationSettings Settings, string? Password, bool ClearPassword = false);
public sealed record MqttDiagnosticsRequest(AutomationRequest Connection, string Prefix = "scrypted");
public sealed record AutomationRuleTestRequest(int SourceSlot);
internal sealed record PendingRuleTest(string RuleId, int SourceSlot, int Revision, CancellationToken Token, TaskCompletionSource<ViewerCommandResult> Completion);
internal sealed record StoredAutomation(AutomationSettings Settings, string ProtectedPassword);
public sealed record AutomationStatus(string Connection, string LastResult, DateTimeOffset? LastMessage,
    DateTimeOffset? LastPerson, object[] Rules);

public sealed class AutomationService : BackgroundService
{
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly ViewerCommandClient _viewer;
    private readonly JsonSettingsStore _cameras;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private StoredAutomation _stored = new(new(), "");
    private int _revision;
    private readonly Channel<PendingRuleTest> _ruleTests = Channel.CreateBounded<PendingRuleTest>(32);
    private AutomationStatus _status = new("Disabled", "Waiting", null, null, []);
    public AutomationStatus Status => _status;
    public MqttDiagnostics Diagnostics { get; } = new();
    public object Configuration => new { settings = _stored.Settings, hasPassword = _stored.ProtectedPassword.Length > 0 };
    public bool UsesLayout(string id) => _stored.Settings.Rules.Any(r => r.Action == AutomationAction.FocusedLayout && r.LayoutId == id);

    public AutomationService(string directory, IDataProtectionProvider protection, ViewerCommandClient viewer)
    {
        _path = Path.Combine(directory, "automation.json");
        _protector = protection.CreateProtector("RTSPView.Mqtt.Password.v1");
        _viewer = viewer;
        _cameras = new(Path.Combine(directory, "settings.json"));
        try
        {
            if (File.Exists(_path)) _stored = JsonSerializer.Deserialize<StoredAutomation>(File.ReadAllText(_path)) ?? throw new InvalidDataException();
        }
        catch { _status = new("Error", "Automation settings could not be loaded. Save valid settings to recover.", null, null, []); }
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
            request.Settings.Validate(await _cameras.LoadAsync(token));
            var password = Password(request);
            if (password.Length > 1024) throw new InvalidDataException("Password is too long.");
            var stored = new StoredAutomation(request.Settings, password.Length == 0 ? "" : _protector.Protect(password));
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }), token);
            File.Move(temp, _path, true);
            _stored = stored;
            Interlocked.Increment(ref _revision);
        }
        finally { _saveGate.Release(); }
    }

    public async Task<ViewerCommandResult> TestRuleAsync(string ruleId, int sourceSlot, CancellationToken token)
    {
        var settings = _stored.Settings;
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
        var topics = settings.Rules.Where(r => r.Enabled).SelectMany(r => r.Sources).Select(s => s.Topic).Distinct().ToArray();
        if (topics.Length == 0) return;
        var builder = new MqttClientSubscribeOptionsBuilder();
        foreach (var topic in topics) builder.WithTopicFilter(topic);
        var result = await client.SubscribeAsync(builder.Build(), token);
        if (result.Items.Any(i => (int)i.ResultCode >= 128)) throw new InvalidDataException("Broker rejected a subscription.");
    }

    public async Task<object> TestAsync(AutomationRequest request, CancellationToken token)
    {
        request.Settings.Validate(await _cameras.LoadAsync(token), true);
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
        var channel = Channel.CreateBounded<(string Topic, byte[] Payload, bool Retain, DateTimeOffset Received)>(new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.DropOldest });
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
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                try
                {
                    var cameras = await _cameras.LoadAsync(stoppingToken);
                    var currentHash = AutomationConfiguration.Hash(cameras);
                    if (revision != _revision || hash != currentHash)
                    {
                        revision = _revision; hash = currentHash;
                        client?.Dispose(); client = null;
                        while (channel.Reader.TryRead(out _)) { }
                        engine.Clear(); sent = ""; lastEvents.Clear(); lastTriggered.Clear(); sentRules.Clear(); nextConnect = now; failures = 0;
                        await Send(engine, hash, stoppingToken);
                    }
                    var settings = _stored.Settings;
                    settings.Validate(cameras);
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
                    _status = _status with { Rules = settings.Rules.Select(r => (object)new { r.Id, r.Name, r.Enabled,
                        lastEvent = lastEvents.TryGetValue(r.Id, out var received) ? (DateTimeOffset?)received : null,
                        lastTriggered = lastTriggered.TryGetValue(r.Id, out var triggered) ? (DateTimeOffset?)triggered : null,
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

    private Task<ViewerCommandResult> Send(PersonOverlayEngine engine, string hash, CancellationToken token) =>
        _viewer.SendAsync(new ViewerCommand(Guid.NewGuid(), ViewerCommandType.AutomationOverlays, Automation: new(hash, engine.Leases.Values.ToArray())), token);
}
