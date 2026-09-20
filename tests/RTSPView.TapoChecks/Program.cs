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
var failDelivery = false;
using var service = new TapoService(directory, protection, (command, _) => { commands.Enqueue(command); return Task.FromResult(new ViewerCommandResult(command.Id, !failDelivery, failDelivery ? "Viewer unavailable." : "Applied")); }, reader);
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
failDelivery = true;
Check(!(await service.TestAsync(rule.Id, ContactState.Open, default)).Success && service.Status.Delivery?.Success == false,
    "Viewer failure was lost from operational status");
failDelivery = false;
await service.TestAsync(rule.Id, ContactState.Open, default);
Check(service.Status.Delivery?.Success == true, "Viewer recovery did not update delivery status");
var staleRevision = AutomationRevision.For(service.CurrentSettings);
await service.SaveAsync(new(settings with { Rules = [rule with { Priority = 1 }] }), default);
Check(service.Status.TestingRules.Contains(rule.Id), "Priority-only save interrupted active simulation");
try { await service.SaveAsync(new(settings, Revision: staleRevision), default); throw new Exception("Stale Tapo settings overwrote priority"); } catch (InvalidDataException) { }
var layoutRule = rule with { Id = Guid.NewGuid().ToString("N"), Action = SensorAction.Layout, LayoutId = alternate.Id, ClearAction = SensorAction.Layout, ClearLayoutId = alternate.Id };
await service.SaveAsync(new(settings with { Rules = [rule, layoutRule] }), default);
await new JsonSettingsStore(Path.Combine(directory, "settings.json")).SaveAsync(app with { Layouts = [app.Layouts[0]] });
reader.Snapshot = new([new(hub.Id, hub.Name, "H100", true, "Connected")], [Reading(ContactState.Open)]);
await Until(() => service.Status.RuleErrors.ContainsKey(layoutRule.Id) && service.Status.ActiveRules.Contains(rule.Id), "Invalid Tapo rule disabled a valid rule");
await service.SaveAsync(new(service.CurrentSettings with { Enabled = false }), default);
await Until(() => service.Status.ActiveRules.Length == 0, "Invalid target prevented disabling Tapo");
await new JsonSettingsStore(Path.Combine(directory, "settings.json")).SaveAsync(app);
await service.SaveAsync(new(settings with { Enabled = false }), default);
await Until(() => commands.Last().Sensors!.Effects.Length == 0, "Disable did not clear active test and effects");
Check(service.Status.TestingRules.Length == 0, "Configuration changes retained tests");
await service.SaveAsync(new(settings), default);
await service.TestAsync(rule.Id, ContactState.Open, default);
await service.RemoveAccountAsync(default);
Check(commands.Last().Sensors!.Effects.Length == 0, "Account removal did not clear active effects");
Check(service.Status.TestingRules.Length == 0, "Account removal retained active tests");
Check(!service.CurrentSettings.Enabled && service.CurrentSettings.Username == "", "Account removal retained connection");
Check(service.CurrentSettings.Rules.Length == settings.Rules.Length && service.CurrentSettings.Hubs.Length == settings.Hubs.Length, "Account removal lost saved rules or hubs");
await service.StopAsync(default);
using var reload = new TapoService(directory, protection, (_, _) => Task.FromResult(new ViewerCommandResult(Guid.NewGuid(), true, "")), new FakeReader(reader.Snapshot));
Check(JsonSerializer.Serialize(reload.Configuration).Contains("hasPassword"), "Saved settings did not reload");
Console.WriteLine("PASS encrypted persistence, actual service polling, partial/offline state, overlay transitions, tests, disable and separate sensor IPC.");
var damagedDirectory = Path.Combine(directory, "damaged"); Directory.CreateDirectory(damagedDirectory);
await File.WriteAllTextAsync(Path.Combine(damagedDirectory, "tapo.json"), "{broken");
using (var damaged = new TapoService(damagedDirectory, protection, (_, _) => Task.FromResult(new ViewerCommandResult(Guid.NewGuid(), true, "Applied")), new FakeReader(new([], []))))
{
    await damaged.StartAsync(default); await Task.Delay(100);
    Check(damaged.Status.ConfigurationError is not null, "Tapo load error disappeared");
    await damaged.SaveAsync(new(new()), default);
    Check(damaged.Status.ConfigurationError is null && Directory.GetFiles(damagedDirectory, "tapo.json.invalid-*").Length == 1, "Tapo load-error recovery lost the damaged file");
    await damaged.StopAsync(default);
}
Console.WriteLine("PASS review regressions: viewer failure/recovery status, priority preserves tests, stale saves rejected, invalid rules isolated, disabled invalid references and damaged configuration recovery.");

var recoveryDirectory = Path.Combine(directory, "recovery"); Directory.CreateDirectory(recoveryDirectory);
var recoveryPath = Path.Combine(recoveryDirectory, "tapo.json");
AutomationPersistence.Save(recoveryPath, "{\"Value\":1}");
AutomationPersistence.Save(recoveryPath, "{\"Value\":2}");
AutomationPersistence.Save(recoveryPath, "{\"Value\":3}");
File.WriteAllText(recoveryPath, "{broken"); File.WriteAllText(recoveryPath + ".bak1", "{broken");
var restored = AutomationPersistence.Load(recoveryPath, new Dictionary<string, int>(), v => { if (!v.ContainsKey("Value")) throw new InvalidDataException(); }, out var recovered);
Check(recovered && restored["Value"] == 1, "Recovery did not skip damaged recent backup");
Check(Directory.GetFiles(recoveryDirectory, "tapo.json.invalid-*").Length == 1, "Recovery lost damaged primary");
var oldPrimary = File.ReadAllText(recoveryPath); var oldBackup = File.ReadAllText(recoveryPath + ".bak1");
AutomationPersistence.Prepare(recoveryDirectory);
AutomationPersistence.Save(recoveryPath, "{\"Value\":4}");
AutomationPersistence.Write(Path.Combine(recoveryDirectory, "automation.json"), "{\"Partial\":true}");
AutomationPersistence.Recover(recoveryDirectory);
Check(File.ReadAllText(recoveryPath) == oldPrimary && File.ReadAllText(recoveryPath + ".bak1") == oldBackup && !File.Exists(Path.Combine(recoveryDirectory, "automation.json")), "Interrupted transaction was not completely undone");
AutomationPersistence.Prepare(recoveryDirectory); AutomationPersistence.Save(recoveryPath, "{\"Value\":5}"); AutomationPersistence.Commit(recoveryDirectory); AutomationPersistence.Recover(recoveryDirectory);
Check(File.ReadAllText(recoveryPath).Contains('5'), "Committed transaction was rolled back");
var historyPath = Path.Combine(recoveryDirectory, "activity.json"); var history = new AutomationActivity(historyPath);
for (var i = 0; i < 210; i++) history.Add("id" + i, "Rule\nname", "Trigger", "Sensor: ShowOverlay", true);
var historyReloaded = new AutomationActivity(historyPath);
Check(historyReloaded.Entries.Length == 200 && historyReloaded.LastTrigger("id209") is not null && historyReloaded.Entries.All(e => !e.Name.Contains('\n')), "Bounded sanitized history did not survive restart");
historyReloaded.Add("id209", "Rule", "State", "Closed", true); historyReloaded.Add("id209", "Rule", "State", "Closed", true);
Check(historyReloaded.Entries.Count(e => e.Kind == "State") == 1, "Repeated sensor renewals flooded history");
var visibilityMqtt = new OverlayAutomationState(); var visibilityTapo = new SensorPresentationState();
visibilityMqtt.Update([new("episode", 10, now.AddMinutes(1), RuleId: "person", Priority: 3)], now);
visibilityTapo.Update(new("hash", now.AddMinutes(1), [new("door", SensorAction.HideOverlay, 10, "", 2)]));
var visibility = AutomationVisibility.Capture(visibilityMqtt, visibilityTapo, app, now, false, true);
Check(visibility.Rules.Single(r => r.RuleId == "door").Effective && !visibility.Rules.Single(r => r.RuleId == "person").Effective, "Overlay winner telemetry disagrees with arbitration");
Check(AutomationVisibility.Capture(visibilityMqtt, visibilityTapo, app, now, true, true).Rules.All(r => !r.Effective), "Manual focus reported automation as effective");
visibilityTapo.Clear(); visibilityMqtt.Dismiss();
Check(AutomationVisibility.Capture(visibilityMqtt, visibilityTapo, app, now, false, true).Rules.Single().Reason == "Manually dismissed", "Dismissal absent from telemetry");
Check(AutomationVisibility.Capture(visibilityMqtt, visibilityTapo, app, now.AddMinutes(2), false, true).Rules.Length == 0, "Expired overrides reported as effective");
visibilityMqtt.Clear(); visibilityMqtt.Update([new("view", 1, now.AddMinutes(1), AutomationAction.FullScreen, RuleId: "person", Priority: 1)], now);
visibilityTapo.Update(new("hash", now.AddMinutes(1), [new("door", SensorAction.Layout, 0, alternate.Id, 2)]));
Check(AutomationVisibility.Capture(visibilityMqtt, visibilityTapo, app, now, false, true).Rules.Single(r => r.Effective).RuleId == "person", "MQTT view priority not reported");
visibilityTapo.Update(new("hash", now.AddMinutes(1), [new("door", SensorAction.Layout, 0, alternate.Id, 1)]));
Check(AutomationVisibility.Capture(visibilityMqtt, visibilityTapo, app, now, false, true).Rules.Single(r => r.Effective).RuleId == "door", "Equal priority did not report Tapo winner");
Check(AutomationVisibility.Capture(visibilityMqtt, visibilityTapo, app, now, false, false).Rules.All(r => !r.Effective && r.Reason == "Viewer hidden"), "Hidden viewer reported effective rules");
visibilityTapo.Clear();
var twoFocusId = focusedApp.AutomationViewLayouts.First(l => l.FocusSlots.Length == 2).Id;
visibilityMqtt.Update([
    new("a", 1, now.AddMinutes(1), AutomationAction.FocusedLayout, now, "first", twoFocusId, Priority: 1),
    new("b", 1, now.AddMinutes(1), AutomationAction.FocusedLayout, now, "duplicate", twoFocusId, Priority: 1),
    new("c", 2, now.AddMinutes(1), AutomationAction.FocusedLayout, now, "second", twoFocusId, Priority: 1)], now);
var focusedVisibility = AutomationVisibility.Capture(visibilityMqtt, visibilityTapo, focusedApp, now, false, true);
Check(focusedVisibility.Rules.Where(r => r.Effective).Select(r => r.RuleId).SequenceEqual(new[] { "first", "second" }), "Two-focus telemetry reported a duplicate rather than the contributing rules");
Console.WriteLine("PASS interrupted/committed transaction recovery, rolling backup fallback, bounded durable history, priority/dismissal/manual-focus telemetry and expiry.");

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
