using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using MQTTnet;
using MQTTnet.Server;
using RTSPView.Controller;
using RTSPView.Core;
using RTSPView.Infrastructure;

static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
static byte[] Event(DateTimeOffset time, string kind = "person") => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
    timestamp = time.ToUnixTimeMilliseconds(), detections = new[] { new { className = kind, id = "same-person" } } }));
static async Task Until(Func<bool> condition, string reason)
{
    var end = DateTime.UtcNow.AddSeconds(12);
    while (!condition() && DateTime.UtcNow < end) await Task.Delay(50);
    Check(condition(), reason);
}

var now = DateTimeOffset.UtcNow;
var rule = new AutomationRule { Name = "Any entrance", Sources = [new(1, "scrypted/44/ObjectDetector"), new(2, "scrypted/45/ObjectDetector")], OverlaySlot = 10, ClearMinutes = 0.1 };
var settings = new AutomationSettings { Enabled = true, Host = "localhost", Rules = [rule] };
var engine = new PersonOverlayEngine();
string Accept(string topic, byte[] bytes, DateTimeOffset received, bool retained = false) => engine.Accept(settings, topic, bytes, retained, received, now.AddSeconds(-1));
Check(Accept(rule.Sources[0].Topic, Event(now), now, true) != "Person detected" && engine.Leases.Count == 0, "Retained true must not activate");
Check(Accept(rule.Sources[0].Topic, Event(now.AddSeconds(-20)), now) != "Person detected", "Stale event accepted");
Check(Accept(rule.Sources[0].Topic, Event(now.AddSeconds(10)), now) != "Person detected", "Future clock accepted");
Check(Accept(rule.Sources[0].Topic, Event(now, "motion"), now) == "No person", "Motion triggered person action");
Check(Accept(rule.Sources[0].Topic, Encoding.UTF8.GetBytes("{"), now) == "Invalid event", "Malformed JSON not rejected");
Check(Accept(rule.Sources[0].Topic, Event(now.AddSeconds(1)), now.AddSeconds(1)) == "Person detected", "First source failed");
var first = engine.Leases[rule.Id];
Check(Accept(rule.Sources[0].Topic, Event(now.AddSeconds(1)), now.AddSeconds(2)) == "Duplicate or out-of-order event", "Duplicate renewed");
Check(engine.Leases[rule.Id] == first, "Duplicate changed expiry");
Check(Accept(rule.Sources[1].Topic, Event(now.AddSeconds(4)), now.AddSeconds(4)) == "Person detected", "OR source failed");
var renewed = engine.Leases[rule.Id];
Check(renewed.Id == first.Id && renewed.ExpiresAt > first.ExpiresAt, "Fresh repeated tracked object must renew episode");
Accept(rule.Sources[0].Topic, Event(now.AddSeconds(2)), now.AddSeconds(4));
Check(engine.Leases[rule.Id].ExpiresAt == renewed.ExpiresAt, "Slower source shortened shared timer");
Accept(rule.Sources[0].Topic, Event(now.AddSeconds(5), "face"), now.AddSeconds(5));
Check(engine.Leases[rule.Id].ExpiresAt == renewed.ExpiresAt, "Face-only event renewed timer");
engine.Expire(now.AddSeconds(11)); Check(engine.Leases.Count == 0, "No-detection timeout did not clear");

var presentation = new OverlayAutomationState();
presentation.Update([first, new("second", 10, now.AddSeconds(15)), new("third", 11, now.AddSeconds(16))], now);
Check(presentation.ActiveSlots(now.AddSeconds(10)).SetEquals([10, 11]), "Shared overlay restored while another rule owns it");
presentation.Dismiss(); Check(presentation.ActiveSlots(now).Count == 0, "Manual dismissal failed");
presentation.Update([first with { ExpiresAt = now.AddSeconds(25) }], now);
Check(presentation.ActiveSlots(now).Count == 0, "Renewal overrode manual dismissal");
presentation.Update([first with { Id = "new-episode", ExpiresAt = now.AddSeconds(20) }], now);
Check(presentation.ActiveSlots(now).SetEquals([10]), "New episode stayed dismissed");
Check(presentation.ActiveSlots(now.AddSeconds(21)).Count == 0, "Viewer failed to expire without Controller");

var focusRule = rule with { Action = AutomationAction.FullScreen, CameraSlot = 0 };
var focusSettings = settings with { Rules = [focusRule] };
var focusEngine = new PersonOverlayEngine();
focusEngine.Accept(focusSettings, rule.Sources[0].Topic, Event(now), false, now, now);
focusEngine.Accept(focusSettings, rule.Sources[1].Topic, Event(now.AddSeconds(3)), false, now.AddSeconds(3), now);
Check(focusEngine.Leases.Count == 2, "Triggering cameras need independent episodes");
presentation.Clear(); presentation.Update(focusEngine.Leases.Values.ToArray(), now.AddSeconds(3));
Check(presentation.Focus(now.AddSeconds(3))?.Slot == 1 && presentation.ActiveSlots(now.AddSeconds(3)).Count == 0, "First focus did not win or leaked into overlays");
focusEngine.Accept(focusSettings, rule.Sources[1].Topic, Event(now.AddSeconds(4)), false, now.AddSeconds(4), now);
presentation.Update(focusEngine.Leases.Values.ToArray(), now.AddSeconds(4));
Check(presentation.Focus(now.AddSeconds(4))?.Slot == 1, "Competing renewal stole focus");
focusEngine.Expire(now.AddSeconds(7)); presentation.Update(focusEngine.Leases.Values.ToArray(), now.AddSeconds(7));
Check(focusEngine.Leases.Count == 1 && presentation.Focus(now.AddSeconds(7))?.Slot == 2, "Cleared camera did not yield to active camera");
var focusedLayoutLease = new AutomationOverlayLease("layout", 3, now.AddSeconds(20), AutomationAction.FocusedLayout, now.AddSeconds(-1));
presentation.Update([..focusEngine.Leases.Values, focusedLayoutLease], now.AddSeconds(7));
Check(presentation.Focus(now.AddSeconds(7))?.Action == AutomationAction.FullScreen, "Fullscreen must take priority over focused layout");
Check(presentation.Focus(now.AddSeconds(11))?.Action == AutomationAction.FocusedLayout, "Focused layout was not restored after fullscreen expired");
presentation.Dismiss();
presentation.Update([..focusEngine.Leases.Values.Select(l => l with { ExpiresAt = now.AddSeconds(30) }), focusedLayoutLease], now.AddSeconds(12));
Check(presentation.Focus(now.AddSeconds(12)) is null, "Manual dismissal did not suppress pending and renewed focus actions");
Check(presentation.Focus(now.AddSeconds(40)) is null, "Viewer did not expire focus without Controller");
focusEngine.Clear(); focusSettings = settings with { Rules = [focusRule with { CameraSlot = 3 }] };
focusEngine.Accept(focusSettings, rule.Sources[0].Topic, Event(now), false, now, now);
focusEngine.Accept(focusSettings, rule.Sources[1].Topic, Event(now.AddSeconds(3)), false, now.AddSeconds(3), now);
Check(focusEngine.Leases.Count == 1 && focusEngine.Leases[rule.Id].Slot == 3 && focusEngine.Leases[rule.Id].ExpiresAt == DateTimeOffset.FromUnixTimeMilliseconds(now.ToUnixTimeMilliseconds()).AddSeconds(9), "Fixed focus target did not share OR clear timer");
Check(JsonSerializer.Deserialize<AutomationRule>("{\"OverlaySlot\":10}")!.Action == AutomationAction.Overlay, "Existing rules must remain overlay actions");

var directory = Path.Combine(Path.GetTempPath(), "rtspview-automation-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var cameras = new AppSettings();
cameras = cameras with { Cameras = cameras.Cameras.Select(c => c with { RtspUrl = "rtsp://example.test/" + c.Slot }).ToArray(),
    DoorbellOverlay = cameras.DoorbellOverlay with { Camera = cameras.DoorbellOverlay.Camera with { RtspUrl = "rtsp://example.test/overlay", Enabled = false } } };
await new JsonSettingsStore(Path.Combine(directory, "settings.json")).SaveAsync(cameras);
settings.Validate(cameras);
focusSettings.Validate(cameras);
try { (settings with { Rules = [rule with { Action = (AutomationAction)99 }] }).Validate(cameras); throw new Exception("Unknown action accepted"); } catch (InvalidDataException) { }
try { (settings with { Rules = [rule with { Action = AutomationAction.FocusedLayout, CameraSlot = 10 }] }).Validate(cameras); throw new Exception("Overlay accepted as focused-layout target"); } catch (InvalidDataException) { }
foreach (var count in Enumerable.Range(1, 16)) foreach (var aspect in new[] { "16:9", "9:16" })
{
    var configured = cameras with { CameraCount = count, Cameras = cameras.Cameras.Select((c, i) => c with { Enabled = i < count }).ToArray(), Layouts = [new() { AspectRatio = aspect }] };
    var beforeHash = AutomationConfiguration.Hash(configured);
    var layout = AutomationConfiguration.FocusedLayout(configured, configured.Cameras[count - 1].Slot);
    Check(layout.Tiles.Count == count && layout.Tiles.Select(t => t.CameraSlot).Distinct().Count() == count, "Focused layout dropped or duplicated a stream");
    Check(layout.AspectRatio == aspect && layout.Tiles[0].CameraSlot == configured.Cameras[count - 1].Slot, "Focused layout lost aspect or target");
    if (count > 1) Check(layout.Tiles[0].RowSpan * layout.Tiles[0].ColumnSpan > layout.Tiles[1].RowSpan * layout.Tiles[1].ColumnSpan, "Focus tile is not larger");
    var occupied = new HashSet<(int, int)>();
    foreach (var tile in layout.Tiles) for (var r = tile.Row; r < tile.Row + tile.RowSpan; r++) for (var c = tile.Column; c < tile.Column + tile.ColumnSpan; c++)
        Check(r < layout.Rows && c < layout.Columns && occupied.Add((r, c)), "Focused layout overlaps or escapes grid");
    Check(AutomationConfiguration.Hash(configured) == beforeHash, "Transient focus changed saved layout");
}
try { (settings with { Rules = [rule with { Sources = [] }] }).Validate(cameras); throw new Exception("No sources accepted"); } catch (InvalidDataException) { }
try { (settings with { Rules = [rule with { Sources = [new(1, "#")] }] }).Validate(cameras); throw new Exception("Wildcard accepted"); } catch (InvalidDataException) { }

var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
using var broker = new MqttFactory().CreateMqttServer(new MqttServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).WithDefaultEndpointPort(port).Build());
await broker.StartAsync();
var pipeName = "rtspview-automation-test-" + Guid.NewGuid().ToString("N");
var commands = new ConcurrentQueue<ViewerCommand>();
using var cancel = new CancellationTokenSource();
var pipeWorker = Task.Run(async () => {
    try {
        while (!cancel.IsCancellationRequested) {
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(cancel.Token);
            using var reader = new StreamReader(pipe); await using var writer = new StreamWriter(pipe) { AutoFlush = true };
            var command = JsonSerializer.Deserialize<ViewerCommand>((await reader.ReadLineAsync(cancel.Token))!)!;
            commands.Enqueue(command);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new ViewerCommandResult(command.Id, true, "Test viewer acknowledged")));
        }
    } catch (OperationCanceledException) { }
});
var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys")));
using var service = new AutomationService(directory, protection, new ViewerCommandClient(pipeName));
var connection = settings with { Host = "127.0.0.1", Port = port };
await service.SaveAsync(new(connection, "test-secret"), CancellationToken.None);
Check(!File.ReadAllText(Path.Combine(directory, "automation.json")).Contains("test-secret"), "Password persisted in plaintext");
Check(!JsonSerializer.Serialize(service.Configuration).Contains("test-secret"), "Password leaked through API");
var test = JsonSerializer.Serialize(await service.TestAsync(new(connection, null), CancellationToken.None));
Check(test.Contains("\"success\":true"), "Draft connection test failed");
Check(commands.IsEmpty, "Test connection executed a viewer command");
await broker.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
    .WithTopic(rule.Sources[0].Topic).WithPayload(Event(DateTimeOffset.UtcNow)).WithRetainFlag().Build()));
await service.StartAsync(CancellationToken.None);
await Until(() => service.Status.Connection == "Connected", "Service never connected");
await Task.Delay(600);
Check(!commands.Any(c => c.Automation?.Leases.Length > 0), "Retained subscription message activated viewer");
JsonElement RuleStatus() => JsonSerializer.SerializeToElement(service.Status.Rules).EnumerateArray().First(r => r.GetProperty("Id").GetString() == rule.Id);
Check(RuleStatus().GetProperty("lastEvent").ValueKind == JsonValueKind.String, "Retained receipt was not reported as an event");
Check(RuleStatus().GetProperty("lastTriggered").ValueKind == JsonValueKind.Null, "Retained receipt was reported as rule execution");
async Task Publish(string topic, DateTimeOffset time) => await broker.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(Event(time)).Build()));
await Publish(rule.Sources[0].Topic, DateTimeOffset.UtcNow);
await Until(() => commands.Any(c => c.Automation?.Leases.Length == 1), "Real MQTT did not produce a viewer lease");
await Until(() => RuleStatus().GetProperty("lastTriggered").ValueKind == JsonValueKind.String, "Successful viewer command did not record the rule trigger time");
var initial = commands.Last(c => c.Automation?.Leases.Length == 1).Automation!.Leases[0];
await Publish(rule.Sources[1].Topic, DateTimeOffset.UtcNow.AddMilliseconds(50));
await Until(() => commands.Any(c => c.Automation?.Leases.Any(l => l.ExpiresAt > initial.ExpiresAt) == true), "Second source did not renew live lease");
await broker.StopAsync();
await Until(() => commands.Last().Automation?.Leases.Length == 0, "Broker outage did not expire the existing lease");
await broker.StartAsync();
await Until(() => service.Status.Connection == "Connected", "Service did not reconnect");
var afterReconnect = commands.Count;
await Task.Delay(600);
Check(commands.Skip(afterReconnect).All(c => c.Automation?.Leases.Length == 0), "Reconnect replayed retained person state");
await Publish(rule.Sources[1].Topic, DateTimeOffset.UtcNow);
await Until(() => commands.Last().Automation?.Leases.Length == 1, "Fresh post-reconnect event failed");
await service.SaveAsync(new(connection with { Enabled = false }, null), CancellationToken.None);
await Until(() => service.Status.Connection == "Disabled" && commands.Last().Automation?.Leases.Length == 0, "Disable did not clear viewer leases");
var countBeforeDiscovery = commands.Count;
await service.SaveAsync(new(connection with { Rules = [focusRule] }, null), CancellationToken.None);
await Until(() => service.Status.Connection == "Connected", "Focus rules did not connect");
await Publish(rule.Sources[0].Topic, DateTimeOffset.UtcNow);
await Publish(rule.Sources[1].Topic, DateTimeOffset.UtcNow.AddMilliseconds(10));
await Until(() => commands.Last().Automation?.Leases.Count(l => l.Action == AutomationAction.FullScreen) == 2, "MQTT did not send two independent fullscreen targets to Viewer");
await service.SaveAsync(new(connection with { Enabled = false }, null), CancellationToken.None);
await Until(() => service.Status.Connection == "Disabled" && commands.Last().Automation?.Leases.Length == 0, "Disable did not release fullscreen targets");
countBeforeDiscovery = commands.Count;
await broker.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
    .WithTopic("homeassistant/binary_sensor/scrypted-test-44/MotionSensor/config")
    .WithPayload("{\"state_topic\":\"scrypted/44/motionDetected\",\"dev\":{\"name\":\"Front Door\"}}")
    .WithRetainFlag().Build()));
await service.StartDiagnosticsAsync(new(new(connection with { Rules = [rule with { Sources = [] }] }, null)), CancellationToken.None);
JsonElement Diagnostics() => JsonSerializer.SerializeToElement(service.Diagnostics.Snapshot());
await Until(() => Diagnostics().GetProperty("connection").GetString() == "Listening", "Discovery never subscribed");
await Publish(rule.Sources[0].Topic, DateTimeOffset.UtcNow);
await Until(() => Diagnostics().GetProperty("topics").EnumerateArray().Any(t => t.GetProperty("Topic").GetString() == rule.Sources[0].Topic && t.GetProperty("CameraName").GetString() == "Front Door" && t.GetProperty("PersonSeen").GetBoolean()), "Observed person topic was not associated with Scrypted camera metadata");
Check(commands.Count == countBeforeDiscovery, "Discovery activated the viewer while automation disabled");
Check(!Diagnostics().GetRawText().Contains("test-secret"), "Discovery exposed connection credentials");
for (var i = 0; i < 205; i++) await broker.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
    .WithTopic("scrypted/44/test").WithPayload(i == 204 ? new string('x', 9000) : i.ToString()).Build()));
await Until(() => Diagnostics().GetProperty("messages").EnumerateArray().Any(m => m.GetProperty("Truncated").GetBoolean()), "Oversized raw payload was not bounded");
Check(Diagnostics().GetProperty("messages").GetArrayLength() == 200, "Raw feed history not bounded at 200");
await service.Diagnostics.StopAsync(CancellationToken.None);
Check(Diagnostics().GetProperty("connection").GetString() == "Stopped", "Discovery did not stop");
Check(Diagnostics().GetProperty("until").ValueKind == JsonValueKind.Null, "Discovery deadline not cleared");
await service.StopAsync(CancellationToken.None);
cancel.Cancel(); await pipeWorker; await broker.StopAsync();
Console.WriteLine("PASS: person parsing, freshness, duplicate rejection, multi-camera OR renewal, shared overlays, manual override, expiry, validation, encrypted secrets, draft test, real MQTT → named pipe, retained state, broker outage, reconnect, and disable.");
Console.WriteLine("Test artifacts: " + directory);
Console.WriteLine("PASS: read-only discovery, Scrypted camera names, incomplete draft rules, person topics, credential privacy, bounded raw feed and stop.");
Console.WriteLine("PASS: fullscreen/focused-layout actions, fixed and triggering targets, competing detections, priority, manual override, offline expiry, legacy rules, validation, and 1–16 landscape/portrait layouts.");
