using RTSPView.Core;
using RTSPView.Infrastructure;

static class SnapshotChecks
{
    public static async Task RunAsync(string root)
    {
        void Check(bool value) { if (!value) throw new Exception("Snapshot scheduling regression"); }
        var schedule = new SnapshotRefreshSchedule(); var now = DateTimeOffset.UtcNow;
        var disabled = new SnapshotSettings(); var enabled = disabled with { Enabled = true };
        Check(!schedule.IsDue(now, disabled)); Check(!schedule.IsDue(now.AddDays(2), disabled));
        Check(!schedule.IsDue(now, enabled)); Check(!schedule.IsDue(now.AddMinutes(59), enabled));
        Check(schedule.IsDue(now.AddHours(1), enabled)); Check(!schedule.IsDue(now.AddHours(1), enabled));
        Check(!schedule.IsDue(now.AddHours(1), enabled with { IntervalHours = 2 }));
        Check(!schedule.IsDue(now.AddHours(2), enabled with { IntervalHours = 2 }));
        Check(schedule.IsDue(now.AddHours(3), enabled with { IntervalHours = 2 }));
        Check(!schedule.IsDue(now.AddHours(10), disabled));
        Check(new SnapshotSettings { IntervalHours = double.NaN }.Normalize().IntervalHours == 1);
        var store = new JsonSettingsStore(Path.Combine(root,"snapshot-settings.json"));
        await store.SaveAsync(new AppSettings { Snapshots = enabled with { IntervalHours = 0.5 } });
        var restored = await store.LoadAsync(); Check(restored.Snapshots.Enabled && restored.Snapshots.IntervalHours == 0.5);
        var export = Path.Combine(root,"snapshot-export.json"); await store.ExportWithoutCredentialsAsync(restored,export);
        var imported = JsonSettingsStore.ParseImport(await File.ReadAllTextAsync(export)); Check(imported.Snapshots == restored.Snapshots);
        Console.WriteLine("PASS snapshot defaults, schedule, interval changes, disable, persistence and export/import");
    }
}
