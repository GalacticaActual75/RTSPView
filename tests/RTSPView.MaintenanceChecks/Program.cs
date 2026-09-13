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

var security = new System.IO.Pipes.PipeSecurity();
security.AddAccessRule(new System.IO.Pipes.PipeAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User!, System.IO.Pipes.PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
var pipeName = "RTSPView-test-" + Guid.NewGuid();
using var pipe = System.IO.Pipes.NamedPipeServerStreamAcl.Create(pipeName, System.IO.Pipes.PipeDirection.InOut, 10, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.FirstPipeInstance, 4096, 4096, security);
Check(pipe.IsAsync, "production pipe options accepted by bundled runtime");
using var service = new MaintenanceService();
var fields = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
typeof(MaintenanceService).GetField("_allowedSid", fields)!.SetValue(service, System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value);
typeof(MaintenanceService).GetField("_engine", fields)!.SetValue(service, engine);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
var accepting = pipe.WaitForConnectionAsync(timeout.Token);
using var client = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification);
await client.ConnectAsync(timeout.Token); await accepting;
var handling = (Task)typeof(MaintenanceService).GetMethod("HandleAsync", fields)!.Invoke(service, new object[] { pipe })!;
using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
using var reader = new StreamReader(client, leaveOpen: true);
await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new MaintenanceRequest("status")));
var response = await reader.ReadLineAsync(timeout.Token);
Check(response is not null && System.Text.Json.JsonSerializer.Deserialize<MaintenanceStatus>(response)!.Available, "authenticated status request reaches actual service handler");
await handling;
