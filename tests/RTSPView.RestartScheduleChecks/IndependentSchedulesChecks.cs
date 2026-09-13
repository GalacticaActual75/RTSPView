using System.Text.Json;
using RTSPView.Controller;
using RTSPView.Core;

internal static class IndependentSchedulesChecks
{
    public static async Task Run(string root)
    {
        var now = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        var calls = new List<string>();
        var updating = false;
        Task<string> Execute(string action, CancellationToken token) { calls.Add(action); return Task.FromResult(action + " accepted"); }
        async Task<bool> Maintenance(Func<Task> work, CancellationToken token) { if (updating) return false; await work(); return true; }
        RestartScheduler Open(string name, DateTimeOffset? start = null) => new(Path.Combine(root, name), Execute, Maintenance, _ => {}, TimeZoneInfo.Utc, start ?? now);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var viewer = new RestartScheduleSettings { Enabled = true, Mode = "interval", IntervalHours = 1 };
        var host = viewer with { Action = "host", IntervalHours = 3 };

        using var scheduler = Open("independent");
        await scheduler.ConfigureAsync(viewer, false, now);
        var viewerNext = (await scheduler.ReadAsync("viewer")).NextRun;
        await scheduler.ConfigureAsync(host, true, now);
        Check((await scheduler.ReadAsync("viewer")).NextRun == viewerNext, "saving host preserves viewer schedule");
        await scheduler.TickAsync(now.AddHours(1));
        Check(calls.SequenceEqual(new[] { "viewer" }), "viewer executes independently before host");
        await scheduler.SkipAsync(now.AddHours(1), "viewer");
        Check((await scheduler.ReadAsync("host")).NextRun == now.AddHours(3), "viewer skip preserves host");
        await scheduler.ConfigureAsync(viewer with { Enabled = false }, false, now.AddHours(1));
        Check((await scheduler.ReadAsync("host")).Settings.Enabled, "disabling viewer leaves host enabled");
        using var restored = Open("independent", now.AddHours(2));
        Check(!(await restored.ReadAsync("viewer")).Settings.Enabled && (await restored.ReadAsync("host")).NextRun == now.AddHours(3), "both slots persist independently");
        await restored.TickAsync(now.AddHours(3));
        Check((await restored.ReadAsync("host")).PendingUntil == now.AddHours(3).AddMinutes(1), "host-only countdown");
        await restored.TickAsync(now.AddHours(3).AddMinutes(1));
        Check(calls.SequenceEqual(new[] { "viewer", "host" }), "host executes independently");
        Check((await restored.ReadAsync("host")).NextRun > now.AddHours(3).AddMinutes(1), "host occurrence remains consumed after result saved");

        calls.Clear();
        using var collision = Open("collision");
        await collision.ConfigureAsync(viewer, false, now);
        await collision.ConfigureAsync(host with { IntervalHours = 1 }, true, now);
        await collision.TickAsync(now.AddHours(1));
        Check(calls.Count == 0 && (await collision.ReadAsync("viewer")).Result.Contains("skipped"), "host countdown takes priority over same-time viewer");
        var viewerAfterCollision = (await collision.ReadAsync("viewer")).NextRun;
        await collision.SkipAsync(now.AddHours(1), "host");
        Check((await collision.ReadAsync("viewer")).NextRun == viewerAfterCollision, "host cancellation does not alter viewer schedule");
        await collision.TickAsync(now.AddHours(1).AddMinutes(1));
        Check(calls.Count == 0, "cancelled host cannot execute and skipped viewer cannot replay");
        await collision.ConfigureAsync(host with { Enabled = false }, false, now.AddHours(1));
        await collision.TickAsync(now.AddHours(2));
        Check(calls.SequenceEqual(new[] { "viewer" }), "viewer continues after host disabled");
        try { await collision.SkipAsync(now, "invalid"); throw new Exception("invalid skip accepted"); } catch (ArgumentException) { }
        try { await collision.ConfigureAsync(host, false, now); throw new Exception("host acknowledgement bypassed"); } catch (ArgumentException) { }
        Check(!(await collision.ReadAsync("host")).Settings.Enabled, "rejected save preserves host state");

        calls.Clear();
        using var deferred = Open("both-deferred");
        await deferred.ConfigureAsync(viewer, false, now);
        await deferred.ConfigureAsync(host with { IntervalHours = 1 }, true, now);
        updating = true;
        await deferred.TickAsync(now.AddHours(1));
        var deferredState = await deferred.ReadAllAsync();
        Check(deferredState.Viewer.Result.Contains("deferred") && deferredState.Host.Result.Contains("deferred") && calls.Count == 0, "update gate defers both schedules");
        updating = false;
        await deferred.TickAsync(now.AddHours(1).AddMinutes(1));
        await deferred.TickAsync(now.AddHours(1).AddMinutes(2));
        Check(calls.SequenceEqual(new[] { "host" }), "deferred collision still runs only host after fresh countdown");

        foreach (var action in new[] { "viewer", "host" })
        {
            var name = "legacy-" + action;
            Directory.CreateDirectory(Path.Combine(root, name));
            var legacy = new RestartScheduleState { Settings = viewer with { Action = action }, NextRun = now.AddHours(1), LastRun = now.AddDays(-1), LastResult = "Previous attempt" };
            File.WriteAllText(Path.Combine(root, name, "restart-schedule.json"), JsonSerializer.Serialize(legacy));
            using var migrated = Open(name);
            var kept = await migrated.ReadAsync(action);
            var other = action == "host" ? "viewer" : "host";
            Check(kept.NextRun == legacy.NextRun && kept.LastResult == legacy.LastResult && !(await migrated.ReadAsync(other)).Settings.Enabled, "legacy " + action + " migrates without enabling other action");
            await migrated.ConfigureAsync(viewer with { Action = other }, other == "host", now);
            using var reopened = Open(name);
            Check((await reopened.ReadAsync(action)).NextRun == legacy.NextRun && (await reopened.ReadAsync(other)).Settings.Enabled, "migrated schedule survives second schedule and reload");
        }
        Console.WriteLine("PASS independent viewer/host schedules, targeted saves/skips/disabling, persistence, legacy migration, collisions, acknowledgement and update deferral. Fake restart commands only.");
    }
}
