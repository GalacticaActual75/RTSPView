using RTSPView.Controller;
using RTSPView.Core;
using RTSPView.Infrastructure;

var directory = Path.Combine(Path.GetTempPath(), "RTSPView-lifecycle-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
try
{
    var state = new ViewerRuntimeState(directory);
    var otherProcess = new ViewerRuntimeState(directory);
    state.Pause();
    Check(otherProcess.Paused, "intentional stop persists across controller instances");
    Check(!await otherProcess.PrepareLaunchAsync(true, timeout.Token), "automatic relaunch honors pause");
    Check(otherProcess.Paused, "automatic relaunch cannot clear intentional stop");
    Check(await otherProcess.PrepareLaunchAsync(false, timeout.Token) && !state.Paused, "desktop launch resumes automatic recovery");
    using (await state.AcquireAsync(timeout.Token))
    {
        using var shortWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try { using var unexpected = await otherProcess.AcquireAsync(shortWait.Token); throw new Exception("Concurrent lifecycle mutation allowed."); }
        catch (OperationCanceledException) { }
    }
    // Stop while recovery is waiting for the gate: recovery must see the pause.
    var held = await state.AcquireAsync(timeout.Token);
    var recovery = otherProcess.PrepareLaunchAsync(true, timeout.Token);
    state.Pause(); held.Dispose();
    Check(!await recovery, "stop wins over queued automatic recovery");
    var starts = 0;
    var running = false;
    var launcher = new ViewerLauncher(state, () => running, () => starts++);
    await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => launcher.StartAsync(timeout.Token)));
    Check(starts == 1 && !state.Paused && launcher.Starting, "concurrent manual start launches only once and resumes recovery");
    running = true;
    await launcher.StartAsync(timeout.Token);
    Check(starts == 1 && !launcher.Starting, "already-running viewer is not relaunched");
    running = false; state.Pause();
    await launcher.StartAsync(timeout.Token);
    Check(starts == 2, "fresh intentional exit allows immediate manual restart");
    state.Pause();
    var failed = new ViewerLauncher(state, () => false, () => throw new IOException("Synthetic missing executable"));
    try { await failed.StartAsync(timeout.Token); throw new Exception("Failed launch accepted."); }
    catch (IOException) { }
    Check(state.Paused && !failed.Starting, "failed launch preserves pause and permits retry");
    var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
    Check(!new AppSettings().ShowHoverExitButton, "hover exit is opt-in");
    await store.SaveAsync(new AppSettings { ShowHoverExitButton = true });
    Check((await store.LoadAsync()).ShowHoverExitButton, "hover preference persists");
    Console.WriteLine("PASS viewer lifecycle: durable pause, automatic/manual launch distinction, queued recovery, concurrent start, running guard, failed start and hover preference.");
}
finally { Directory.Delete(directory, true); }

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
