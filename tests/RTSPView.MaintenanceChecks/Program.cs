using RTSPView.Core;
using RTSPView.Maintenance;

static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
static async Task Reject(Func<Task> action) { try { await action(); } catch (ArgumentException) { return; } throw new Exception("Unsafe request accepted"); }
var installed = false; var installs = 0; var states = new List<MaintenanceStatus>();
var engine = new MaintenanceEngine(() => installed, () => Task.CompletedTask, () => { installs++; installed = true; return Task.FromResult(0); }, states.Add);
await Reject(() => engine.HandleAsync(new("execute")));
await Reject(() => engine.HandleAsync(new("install", Guid.NewGuid())));
var prepared = await engine.HandleAsync(new("prepare"));
await Reject(() => engine.HandleAsync(new("install", Guid.NewGuid())));
Check(installs == 0, "unverified and arbitrary commands cannot install");
var completed = await engine.HandleAsync(new("install", prepared.OperationId));
Check(completed.State == "complete" && installs == 1, "verified operation installs once");
await engine.HandleAsync(new("install", prepared.OperationId));
Check(installs == 1, "installed dependency is not reinstalled");
var failed = new MaintenanceEngine(() => false, () => throw new IOException(), () => { installs++; return Task.FromResult(0); }, _ => { });
Check((await failed.HandleAsync(new("prepare"))).State == "failed" && installs == 1, "download failure never invokes installer");
var reboot = new MaintenanceEngine(() => false, () => Task.CompletedTask, () => Task.FromResult(3010), _ => { });
prepared = await reboot.HandleAsync(new("prepare"));
completed = await reboot.HandleAsync(new("install", prepared.OperationId));
Check(completed.RestartRequired, "reboot requirement reported without restarting host");
var restored = new MaintenanceEngine(() => false, () => Task.CompletedTask, () => Task.FromResult(0), _ => { }, completed);
Check(restored.Status.RestartRequired, "restart notice survives helper restart");
var interrupted = new MaintenanceEngine(() => false, () => Task.CompletedTask, () => Task.FromResult(0), _ => { }, prepared);
await Reject(() => interrupted.HandleAsync(new("install", prepared.OperationId)));
Check(interrupted.Status.State == "failed", "interrupted operation cannot replay after restart");
var blocker = new TaskCompletionSource();
var concurrent = new MaintenanceEngine(() => false, () => blocker.Task, () => Task.FromResult(0), _ => { });
var pending = concurrent.HandleAsync(new("prepare"));
Check((await concurrent.HandleAsync(new("prepare"))).State == "downloading", "concurrent work returns active status");
blocker.SetResult(); await pending;
try { HelperSetup.ValidateProtectedPath(Path.GetTempPath()); throw new Exception("Unprotected path accepted"); }
catch (InvalidOperationException) { Console.WriteLine("PASS helper rejects unprotected install location"); }
