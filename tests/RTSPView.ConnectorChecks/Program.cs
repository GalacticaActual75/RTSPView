using System.Text.Json;
using RTSPView.Core;
using RTSPView.Controller;

static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected rejection"); }
await PasswordRecoveryChecks.Run();
var instance = Guid.NewGuid().ToString();
var sync = new ConnectorSync(1, instance, [new("camera-1", "Driveway", "rtsp://scrypted-host:34197/main", "rtspview/test/ObjectDetector")], null);
var initial = new AppSettings();
var first = ConnectorImport.Merge(initial, sync);
Check(first.Cameras[0].ScryptedId == instance + ":camera-1", "Identity not saved");
var tuned = first with { Cameras = first.Cameras.Select(c => c.Slot == 1 ? c with {NetworkCacheMilliseconds = 2300, Enabled = false} : c).ToArray() };
var second = ConnectorImport.Merge(tuned, sync with { Cameras = [sync.Cameras[0] with { Name = "Renamed", RtspUrl = "rtsp://scrypted-host:34197/new" }] });
Check(second.Cameras.Count(c => c.RtspUrl.Length > 0) == 1, "Sync created duplicates");
Check(second.Cameras[0].Name == "Renamed" && second.Cameras[0].NetworkCacheMilliseconds == 2300 && !second.Cameras[0].Enabled, "Playback settings not preserved");
Check(second.Layouts == tuned.Layouts && second.ActiveLayoutId == tuned.ActiveLayoutId, "Layout changed");
var existing = initial with { Cameras = initial.Cameras.Select(c => c.Slot == 3 ? c with {RtspUrl = sync.Cameras[0].RtspUrl} : c).ToArray() };
Check(ConnectorImport.Merge(existing, sync).Cameras[2].ScryptedId.Length > 0, "Matching existing URL was duplicated");
Reject(() => ConnectorImport.Merge(initial, sync with {Cameras = [sync.Cameras[0], sync.Cameras[0]]}));
Reject(() => ConnectorImport.Merge(initial, sync with {Cameras = [sync.Cameras[0] with {RtspUrl = "rtsp://localhost/main"}]}));
Reject(() => ConnectorImport.Merge(initial, sync with {Cameras = [sync.Cameras[0] with {Topic = "camera/#"}]}));
var full = initial with {Cameras = initial.Cameras.Select(c => c with {RtspUrl = "rtsp://existing-host/" + c.Slot}).ToArray()};
Reject(() => ConnectorImport.Merge(full, sync));
var all = ConnectorImport.Merge(initial, sync with {Cameras = Enumerable.Range(1,16).Select(i => sync.Cameras[0] with {Id = "id"+i, RtspUrl = "rtsp://scrypted-host/"+i}).ToArray()});
Check(all.CameraCount == 16 && all.Cameras[9].Slot == 26, "Extended main slots not mapped correctly");
Check(ConnectorImport.BrokerSettings(new(), new("broker-host",1883,false,"", "")).Enabled == false, "Sync enabled automation");

var directory = Path.Combine(Path.GetTempPath(), "rtspview-connector-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var clock = new TestClock();
var pairing = new ConnectorPairing(directory, clock);
string Code() => JsonSerializer.SerializeToElement(pairing.CreateCode("session")).GetProperty("code").GetString()!;
var code = Code();
Check(await pairing.PairAsync("wrong", instance, "session") is null, "Bad code accepted");
var token = await pairing.PairAsync(code, instance, "session");
Check(token is not null && pairing.Authorize(token, instance, "session"), "Valid pairing failed");
Check(await pairing.PairAsync(code, instance, "session") is null, "One-time code reused");
Check(!pairing.Authorize(token!, Guid.NewGuid().ToString(), "session"), "Another instance authorized");
Check(!pairing.Authorize(token!, instance, "changed-session"), "Password change did not invalidate pairing");
var persisted = File.ReadAllText(Path.Combine(directory, "scrypted-connector.json"));
Check(!persisted.Contains(token!) && !persisted.Contains(code), "Plaintext token/code stored");
Check(new ConnectorPairing(directory).Authorize(token!, instance, "session"), "Pairing did not survive restart");
code = Code(); clock.Now = clock.Now.AddMinutes(6);
Check(await pairing.PairAsync(code, instance, "session") is null, "Expired code accepted");
pairing.Revoke(); Check(!pairing.Authorize(token!, instance, "session"), "Revoked token accepted");
Console.WriteLine("PASS: stable imports, existing URL adoption, playback/layout preservation, 16-slot mapping, invalid inputs, capacity, pairing expiry, one-time codes, hashed tokens, restart, password invalidation and revocation.");

sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
