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

var directory = Path.Combine(Path.GetTempPath(), "rtspview-automation-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var cameras = new AppSettings();
cameras = cameras with { Cameras = cameras.Cameras.Select(c => c with { RtspUrl = "rtsp://example.test/" + c.Slot }).ToArray(),
    DoorbellOverlay = cameras.DoorbellOverlay with { Camera = cameras.DoorbellOverlay.Camera with { RtspUrl = "rtsp://example.test/overlay", Enabled = false } } };
await new JsonSettingsStore(Path.Combine(directory, "settings.json")).SaveAsync(cameras);
settings.Validate(cameras);
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
async Task Publish(string topic, DateTimeOffset time) => await broker.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(Event(time)).Build()));
await Publish(rule.Sources[0].Topic, DateTimeOffset.UtcNow);
await Until(() => commands.Any(c => c.Automation?.Leases.Length == 1), "Real MQTT did not produce a viewer lease");
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
