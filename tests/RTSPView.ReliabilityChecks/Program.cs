using System.Text.Json;
using RTSPView.Core;
using RTSPView.Infrastructure;
using RTSPView.Controller;

var root = Path.Combine(Path.GetTempPath(), "RTSPView-reliability-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
var logs = Path.Combine(root, "logs");
var logger = new RollingFileLogger(logs, 1024);
for (var i = 0; i < 100; i++) logger.Write("TEST", new string('x', 700));
Check(Directory.GetFiles(logs).Length <= 14 && Directory.GetFiles(logs).All(p => new FileInfo(p).Length <= 1024), "Logs exceeded byte/file limits.");
var blocked = Path.Combine(root, "not-a-directory"); File.WriteAllText(blocked, "fixture");
var unavailable = new RollingFileLogger(blocked); unavailable.Write("TEST", "rtsp://secret:password@example.test/private");
Check(unavailable.DroppedEntries == 1 && unavailable.LastError is not null && !unavailable.LastError.Contains("secret"), "Log storage failure escaped or leaked data.");
File.Delete(blocked); unavailable.Write("TEST", "recovered");
Check(unavailable.LastError is null && unavailable.DroppedEntries == 1, "Logger did not recover after storage returned.");
Console.WriteLine("PASS bounded log rotation, storage failure isolation, redacted health and recovery");
var state = Path.Combine(root, "host-state.json");
DurableJson.Write(state, new Selection("stable")); DurableJson.Write(state, new Selection("beta"));
File.WriteAllText(state, "{broken");
var recovered = DurableJson.Read(state, () => new Selection("beta"), value => UpdateRelease.ValidateChannel(value.Channel));
Check(recovered.Channel == "stable" && File.ReadAllText(state) == "{broken" && Directory.GetFiles(root, "host-state.json.invalid-*").Length > 0, "Backup recovery destroyed corrupt evidence.");
DurableJson.Write(state, new Selection("stable"));
var prior = File.ReadAllText(state);
using (var locked = new FileStream(state, FileMode.Open, FileAccess.Read, FileShare.None))
{
    try { DurableJson.Write(state, new Selection("beta")); throw new Exception("Locked state unexpectedly replaced."); } catch (IOException) { }
}
Check(File.ReadAllText(state) == prior && Directory.GetFiles(root, "*.tmp").Length == 0, "Failed write damaged the primary or left incomplete temporary state.");
var channels = new UpdateChannelStore(root); channels.Save("stable"); channels.Save("beta");
File.WriteAllText(Path.Combine(root, "update-channel.json"), "null");
Check(channels.Read("beta") == "stable" && channels.RecoveryWarning is not null, "Update channel failed to recover.");
File.WriteAllText(Path.Combine(root, "update-channel.json.bak"), "{bad");
Check(channels.Read("beta") == "beta", "Invalid optional state blocked safe defaults.");
Console.WriteLine("PASS atomic host-state writes, corrupt-state evidence, valid backups and safe defaults");
var settingsPath = Path.Combine(root, "settings.json"); var store = new JsonSettingsStore(settingsPath);
await store.SaveAsync(new AppSettings()); await store.SaveAsync((await store.LoadAsync()) with { ShowCameraNames = false });
File.WriteAllText(settingsPath, "{\"Cameras\":[null]}");
Check((await store.LoadAsync()).ShowCameraNames, "Malformed nested settings did not recover from the valid backup.");
Check(Directory.GetFiles(root, "settings.json.invalid-*").Length > 0, "Settings recovery did not preserve evidence.");
File.Delete(settingsPath);
Check((await store.LoadAsync()).ShowCameraNames && File.Exists(settingsPath), "Missing primary did not restore its valid backup.");
var failedNotice = Path.Combine(root, "update-notice.json");Directory.CreateDirectory(failedNotice);
using var monitor = new UpdateMonitor(root, () => new UpdateStatus("1.0.46-beta.1", null, false, false, "Fixture", "fixture", "beta", "beta"), _ => throw new Exception("No network"), (_,_,_) => throw new Exception("No installer"), _ => { });
Console.WriteLine("PASS malformed nested configuration recovery and optional update-notice storage failure do not crash startup");
record Selection(string Channel);
