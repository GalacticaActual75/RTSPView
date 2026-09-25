using RTSPView.Controller;

internal static class StartupSettingsChecks
{
    public static async Task Run()
    {
        var current = new StartupStatus(true, true, true, "Repair needed");
        var calls = 0;
        var service = new StartupService(true, () => Task.FromResult(current), enabled => { calls++; current = new(enabled,true,false,"Verified"); return Task.CompletedTask; });
        if (!(await service.StatusAsync()).RepairNeeded) throw new Exception("Legacy startup repair state missing");
        if ((await service.SetAsync(true)).RepairNeeded || calls != 1) throw new Exception("Enabled startup did not repair legacy task");
        if ((await service.SetAsync(false)).Enabled || calls != 2) throw new Exception("Startup did not disable");
        var cancelled = new StartupService(true, () => Task.FromResult(current), _ => throw new InvalidOperationException("Approval cancelled"));
        try { await cancelled.SetAsync(true); throw new Exception("Cancelled startup change reported success"); } catch (InvalidOperationException) { }
        if (current.Enabled) throw new Exception("Cancelled change modified startup state");
        var unverified = new StartupService(true, () => Task.FromResult(current), _ => Task.CompletedTask);
        try { await unverified.SetAsync(true); throw new Exception("Unverified startup change reported success"); } catch (InvalidOperationException) { }
        var unavailable = new StartupService(true);
        if ((await unavailable.StatusAsync()).Managed) throw new Exception("Development/test process can change host startup");
        try { await unavailable.SetAsync(true); throw new Exception("Unmanaged startup accepted mutation"); } catch (InvalidOperationException) { }
        Console.WriteLine("PASS startup setting: actual status, legacy repair, disable, approval cancellation, post-write verification and unmanaged host protection.");
    }
}
