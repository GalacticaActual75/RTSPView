using RTSPView.Core;
using RTSPView.Maintenance;

if (args.Length == 2 && args[0] == "--pipe-probe")
{
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    using var peer = new System.IO.Pipes.NamedPipeClientStream(".", args[1], System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.None);
    await peer.ConnectAsync(limit.Token);
    using var send = new StreamWriter(peer, leaveOpen: true) { AutoFlush = true };
    using var receive = new StreamReader(peer, leaveOpen: true);
    await send.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new MaintenanceRequest("status")));
    var reply = await receive.ReadLineAsync(limit.Token);
    if (System.Text.Json.JsonSerializer.Deserialize<MaintenanceStatus>(reply ?? "")?.Available != true)
        throw new Exception("Separate process could not get helper status: " + reply);
    return;
}

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

foreach (var rejected in new[] { "account", "json", "command" })
{
    var name = "RTSPView-rejected-" + Guid.NewGuid();
    using var server = System.IO.Pipes.NamedPipeServerStreamAcl.Create(name, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous, 4096, 4096, security);
    typeof(MaintenanceService).GetField("_allowedSid", fields)!.SetValue(service, rejected == "account" ? "S-1-5-18" : System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var connected = server.WaitForConnectionAsync(deadline.Token);
    using var peer = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification);
    await peer.ConnectAsync(deadline.Token); await connected;
    var task = (Task)typeof(MaintenanceService).GetMethod("HandleAsync", fields)!.Invoke(service, new object[] { server })!;
    using var send = new StreamWriter(peer, leaveOpen: true) { AutoFlush = true };
    using var receive = new StreamReader(peer, leaveOpen: true);
    await send.WriteLineAsync(rejected == "json" ? "not-json" : System.Text.Json.JsonSerializer.Serialize(new MaintenanceRequest(rejected == "command" ? "execute" : "status")));
    var reply = await receive.ReadLineAsync(deadline.Token);
    var rejectedStatus = System.Text.Json.JsonSerializer.Deserialize<MaintenanceStatus>(reply ?? "")!;
    Check(!rejectedStatus.Available && rejectedStatus.State == "failed" && !string.IsNullOrWhiteSpace(rejectedStatus.Message) && rejectedStatus.Temperatures is null, rejected + " rejection returns a clear response without sensor data");
    await task;
}

var childPipeName = "RTSPView-process-" + Guid.NewGuid();
using var childPipe = System.IO.Pipes.NamedPipeServerStreamAcl.Create(childPipeName, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous, 4096, 4096, security);
using var childLimit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var childConnected = childPipe.WaitForConnectionAsync(childLimit.Token);
var childStart = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
childStart.ArgumentList.Add("--pipe-probe"); childStart.ArgumentList.Add(childPipeName);
using var child = System.Diagnostics.Process.Start(childStart)!;
await childConnected;
await (Task)typeof(MaintenanceService).GetMethod("HandleAsync", fields)!.Invoke(service, new object[] { childPipe })!;
await child.WaitForExitAsync(childLimit.Token);
Check(child.ExitCode == 0, "separate Controller process authenticates without impersonation");

foreach (var selection in new[] { ("beta", "../installer.exe"), ("other", "1.0.40"), ("stable", "1.0.40-beta.12") })
{
    try { ServiceUpdateCoordinator.ValidateSelection(selection.Item1, selection.Item2); throw new Exception("Invalid app update selection accepted"); }
    catch (InvalidDataException) { }
}
ServiceUpdateCoordinator.ValidateSelection("beta", "1.0.40-beta.12");
Check(!new RTSPView.Infrastructure.UpdateManifest("1.0.39", "installer.exe", "", DateTimeOffset.UtcNow).SupportsServiceUpdates,
    "older manifests require the UAC path by default");
var appBlocker = new TaskCompletionSource<MaintenanceStatus>();
var sharedGate = new MaintenanceEngine(() => false, () => throw new Exception("PawnIO raced update"), () => Task.FromResult(0), _ => { });
var appTask = sharedGate.RunAppUpdateAsync(() => appBlocker.Task);
Check(!sharedGate.TryBeginStop() && (await sharedGate.HandleAsync(new("prepare"))).State == "downloading", "update verification excludes driver installation and service shutdown");
appBlocker.SetResult(new() { Available = true, State = "update-installing" }); await appTask;
Check(sharedGate.TryBeginStop(), "service can stop after handing installation to independent worker");

var historyDirectory = Path.Combine(Path.GetTempPath(), "RTSPView-update-history-" + Guid.NewGuid().ToString("N"));
try
{
    var operation = Guid.NewGuid();
    var jobDirectory = Path.Combine(historyDirectory, "Updates", operation.ToString("N"));
    Directory.CreateDirectory(jobDirectory);
    File.WriteAllText(Path.Combine(historyDirectory, "app-update.json"), operation.ToString());
    File.WriteAllText(Path.Combine(jobDirectory, "progress.json"), """
        {"state":"failed","message":"Installer exited with code 5.","updatedAt":"2026-09-22T19:52:28Z"}
        """);
    var history = new ServiceUpdateCoordinator(historyDirectory, historyDirectory, "unused");
    Check(history.Status is { State: "update-failed" } status && status.Message.Contains("Last update attempt (") && status.Message.Contains("2026-09-22"),
        "persisted failure is dated and identified as the last attempt, not a new installer run");
}
finally { Directory.Delete(historyDirectory, true); }
