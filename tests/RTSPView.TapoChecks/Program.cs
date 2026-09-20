using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using RTSPView.Controller;
using RTSPView.Core;
using RTSPView.Infrastructure;

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Invalid configuration accepted"); }
static async Task Until(Func<bool> predicate, string message) { for (var i = 0; i < 300; i++) { if (predicate()) return; await Task.Delay(50); } throw new Exception(message); }
var now = DateTimeOffset.UtcNow;
var hub = new TapoHub(Guid.NewGuid().ToString("N"), "Hub", "hub.example");
var app = new AppSettings().Normalize();
app = app with { DoorbellOverlay = app.DoorbellOverlay with { Camera = app.DoorbellOverlay.Camera with { RtspUrl = "rtsp://camera.example/live", Enabled = false } } };
var alternate = app.Layouts[0] with { Id = Guid.NewGuid().ToString("N"), Name = "Garage" };
app = app with { Layouts = app.Layouts.Append(alternate).ToArray() };
var rule = new TapoRule { HubId = hub.Id, DeviceId = "door", OverlaySlot = 10, Action = SensorAction.ShowOverlay, ClearAction = SensorAction.HideOverlay, UnavailableAction = SensorAction.Restore, Priority = 2 };
var settings = new TapoSettings { Enabled = true, Username = "test@example.invalid", Hubs = [hub], Rules = [rule] };
settings.Validate(app);
var focusedApp = app with { Cameras = app.Cameras.Select(c => c with { Enabled = true, RtspUrl = "rtsp://camera.example/live" }).ToArray() };
var focusedRule = rule with { Action = SensorAction.AutomationLayout, LayoutId = focusedApp.AutomationViewLayouts.First(l => l.FocusSlots.Length == 2).Id, FocusCameraSlot = 1, SecondFocusCameraSlot = 2 };
(settings with { Rules = [focusedRule] }).Validate(focusedApp);
Reject(() => (settings with { Rules = [focusedRule with { SecondFocusCameraSlot = 1 }] }).Validate(focusedApp));
Reject(() => (settings with { Rules = [focusedRule with { LayoutId = "missing" }] }).Validate(focusedApp));
var ordering = AutomationPriorityEndpoints.Snapshot(new AutomationSettings { Rules = [new AutomationRule { Name = "Person", Priority = 2 }] }, settings);
AutomationPriorityEndpoints.Validate(ordering with { Rules = ordering.Rules.Reverse().ToArray() }, ordering);
Reject(() => AutomationPriorityEndpoints.Validate(ordering with { Revision = "stale" }, ordering));
Reject(() => AutomationPriorityEndpoints.Validate(ordering with { Rules = [ordering.Rules[0], ordering.Rules[0]] }, ordering));
TapoSensor Reading(ContactState state) => new(hub.Id, "door", "Garage door", "T110", state);
Check(TapoRuleEngine.Evaluate(settings, [Reading(ContactState.Open)]).Single().Action == SensorAction.ShowOverlay, "Open must show overlay");
Check(TapoRuleEngine.Evaluate(settings, [Reading(ContactState.Closed)]).Single().Action == SensorAction.HideOverlay, "Closed must hide overlay");
Check(TapoRuleEngine.Evaluate(settings, []).Length == 0, "Missing sensor must restore, not count as closed");
Check(TapoRuleEngine.Evaluate(settings with { Enabled = false }, [Reading(ContactState.Open)]).Length == 0, "Disabled integration fired");
Reject(() => (settings with { PollSeconds = 0 }).Validate(app));
Reject(() => (settings with { Hubs = [hub, hub] }).Validate(app));
Reject(() => (settings with { Rules = [rule with { Priority = 0 }] }).Validate(app));
Reject(() => (settings with { Rules = [rule with { Action = SensorAction.Layout, LayoutId = "missing" }] }).Validate(app));
Reject(() => (settings with { Hubs = [hub with { Host = "https://hub.example/path" }] }).Validate(app));

var garage = new AutomationRule { Priority = 2, Action = AutomationAction.FocusedLayout, CameraSlot = 2, Sources = [new(2, "garage")], ClearMinutes = 2, AllowNewerDetection = false };
var doorbell = garage with { Id = Guid.NewGuid().ToString("N"), Priority = 1, CameraSlot = 1, Sources = [new(1, "doorbell")], ClearMinutes = 0.1 };
var engine = new PersonOverlayEngine(); var presentation = new OverlayAutomationState();
engine.Trigger(garage, 2, now); engine.Trigger(doorbell, 1, now.AddSeconds(1));
presentation.Update(engine.Leases.Values.ToArray(), now.AddSeconds(1));
Check(presentation.Focus(now.AddSeconds(1))?.Slot == 1, "Priority 1 must interrupt priority 2 despite Hold setting");
engine.Trigger(garage, 2, now.AddSeconds(2)); presentation.Update(engine.Leases.Values.ToArray(), now.AddSeconds(2));
Check(presentation.Focus(now.AddSeconds(2))?.Slot == 1, "Lower-priority renewal stole focus");
Check(presentation.Focus(now.AddSeconds(8))?.Slot == 2, "Garage did not resume after priority 1 cleared");
Check(presentation.Focus(now.AddMinutes(3)) is null, "Expired garage did not restore normal wall");
var legacy = JsonSerializer.Deserialize<AutomationRule>("{\"Name\":\"Existing rule\"}")!;
Check(legacy.Priority == 50, "Existing rules need a compatible default priority");

var sensor = new SensorPresentationState();
sensor.Update(new("hash", now.AddSeconds(5), [new(rule.Id, SensorAction.Layout, 0, alternate.Id, 2), new("overlay", SensorAction.HideOverlay, 10, "", 2)]));
var highDetection = engine.Leases.Values.Single(l => l.Priority == 1);
Check(sensor.WinningLayout(highDetection, now) is null, "Sensor layout defeated higher-priority person detection");
Check(sensor.WinningLayout(highDetection with { Priority = 3 }, now) == alternate.Id, "Sensor layout did not beat lower priority");
Check(sensor.WinningOverlay(10, 1, now) is null, "Low-priority sensor hide defeated high-priority overlay detection");
Check(sensor.WinningOverlay(10, 3, now) == false, "Sensor hide not applied over lower-priority overlay");
Check(sensor.Layout(now.AddSeconds(6)) is null && sensor.Overlay(10, now.AddSeconds(6)) is null, "Viewer-local timeout did not restore defaults");
Console.WriteLine("PASS sensor states, overlay gating, unavailable semantics, shared priority/preemption/resume, legacy priority and viewer-local expiry.");

var directory = Path.Combine(Path.GetTempPath(), "rtspview-tapo-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
await new JsonSettingsStore(Path.Combine(directory, "settings.json")).SaveAsync(app);
var protection = new EphemeralDataProtectionProvider();
var reader = new FakeReader(new([new(hub.Id, hub.Name, "H100", true, "Connected")], [Reading(ContactState.Open)]));
var commands = new ConcurrentQueue<ViewerCommand>();
using var service = new TapoService(directory, protection, (command, _) => { commands.Enqueue(command); return Task.FromResult(new ViewerCommandResult(command.Id, true, "Applied")); }, reader);
await service.SaveAsync(new(settings, "synthetic-test-only"), default);
Check(!File.ReadAllText(Path.Combine(directory, "tapo.json")).Contains("synthetic-test-only"), "Saved password is plaintext");
Check(!JsonSerializer.Serialize(service.Configuration).Contains("synthetic-test-only"), "Configuration leaked password");
await service.StartAsync(default);
await Until(() => commands.LastOrDefault()?.Sensors?.Effects.Any(e => e.Action == SensorAction.ShowOverlay) == true, "Fresh open reading did not reach viewer");
reader.Snapshot = new([new(hub.Id, hub.Name, "H200", true, "Connected")], [Reading(ContactState.Closed)]);
await Until(() => commands.LastOrDefault()?.Sensors?.Effects.Any(e => e.Action == SensorAction.HideOverlay) == true, "Closed did not hide overlay");
reader.Snapshot = new([new(hub.Id, hub.Name, "H200", false, "Unavailable")], []);
await Until(() => service.Status.Sensors.Single().State == ContactState.Unavailable && commands.LastOrDefault()?.Sensors?.Effects.Length == 0, "Lost hub did not restore defaults");
await service.TestAsync(rule.Id, ContactState.Open, default);
Check(commands.Last().Sensors!.Effects.Single().Action == SensorAction.ShowOverlay, "Rule test did not apply");
await service.SaveAsync(new(settings with { Enabled = false }), default);
await Until(() => commands.Last().Sensors!.Effects.Length == 0, "Disable did not clear active test and effects");
Check(service.Status.TestingRules.Length == 0, "Configuration changes retained tests");
await service.StopAsync(default);
using var reload = new TapoService(directory, protection, (_, _) => Task.FromResult(new ViewerCommandResult(Guid.NewGuid(), true, "")), new FakeReader(reader.Snapshot));
Check(JsonSerializer.Serialize(reload.Configuration).Contains("hasPassword"), "Saved settings did not reload");
Console.WriteLine("PASS encrypted persistence, actual service polling, partial/offline state, overlay transitions, tests, disable and separate sensor IPC.");

if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Tapo", "tapo-reader.exe")))
{
    using var packaged = new TapoProcessReader();
    for (var i = 0; i < 2; i++)
    {
        var snapshot = await packaged.ReadAsync(new("package-test@example.invalid", "synthetic-test-only", []), default);
        Check(snapshot.Hubs.Length == 0 && snapshot.Sensors.Length == 0, "Packaged reader protocol failed");
    }
    Console.WriteLine("PASS actual Controller-to-packaged-reader protocol and process reuse, without live hubs.");
}

sealed class FakeReader(TapoSnapshot snapshot) : ITapoReader
{
    public TapoSnapshot Snapshot { get; set; } = snapshot;
    public Task<TapoSnapshot> ReadAsync(TapoConnection connection, CancellationToken token) => Task.FromResult(Snapshot);
    public void Dispose() { }
}
