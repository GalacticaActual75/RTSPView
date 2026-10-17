using RTSPView.Controller;
using RTSPView.Core;

internal static class PatchUpdateChecks
{
    public static async Task Run(string root)
    {
        var directory = Path.Combine(root, "patches");
        var now = DateTimeOffset.UtcNow;
        var installed = UpdateRelease.Parse("1.0.47-beta.12");
        string? available = installed.Label + "-patch.2";
        var checks = 0; var launches = 0; Action? preparing = null;
        PatchUpdateMonitor Monitor() => new(directory, installed, _ => { checks++; return Task.FromResult<string?>(available); },
            (_, enabled, _) => { preparing?.Invoke(); if (!enabled()) return Task.FromResult(new UpdateLaunchResult(false, "Canceled"));
                launches++; return Task.FromResult(new UpdateLaunchResult(true, "Started")); });
        var monitor = Monitor();
        Check(!monitor.Status.AutoInstall, "automatic installation is opt-in");
        await monitor.CheckAsync(false, now);
        await monitor.CheckAsync(false, now.AddHours(1));
        Check(checks == 1 && monitor.Status.AvailableVersion == available, "daily checks still find patches with auto-install off");
        Check(await monitor.InstallScheduledAsync(default) is null && launches == 0, "off prevents installation");
        monitor.SetAutoInstall(true);
        Check(Monitor().Status.AutoInstall, "preference persists across restart");
        preparing = () => monitor.SetAutoInstall(false);
        Check((await monitor.InstallScheduledAsync(default))?.Started == false && launches == 0, "opting out during preparation prevents handoff");
        Check(!Monitor().Status.AutoInstall, "opt-out persists");
        preparing = null;
        monitor.SetAutoInstall(true);
        Check((await monitor.InstallScheduledAsync(default))?.Started == true && launches == 1, "enabled scheduled patch runs");
        var upgraded = new PatchUpdateMonitor(directory, UpdateRelease.Parse(available!), _ => Task.FromResult<string?>(null),
            (_, _, _) => throw new Exception("Unexpected installation"));
        Check(upgraded.Status.AutoInstall && upgraded.Status.AvailableVersion is null && upgraded.Status.LastResult!.Contains("installed"), "patch startup retains preference and records installed result");
        available = "1.0.48-beta.1-patch.1";
        await monitor.CheckAsync(false, now.AddDays(1));
        Check(monitor.Status.AvailableVersion is null, "patch cannot advance base release");
        available = "1.0.47-beta.13";
        await monitor.CheckAsync(false, now.AddDays(2));
        Check(monitor.Status.AvailableVersion is null, "ordinary releases never become automatic patches");
        File.WriteAllText(Path.Combine(directory, "patch-updates.json"), "broken");
        Check(!Monitor().Status.AutoInstall, "corrupt settings fail closed");
        Console.WriteLine("Patch checks passed: daily discovery, persistence, opt-out, in-flight cancellation and release boundaries.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
