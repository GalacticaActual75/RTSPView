using RTSPView.Controller;
using RTSPView.Core;
using RTSPView.Infrastructure;

var root = Path.Combine(Path.GetTempPath(), "RTSPView-schedule-checks-" + Guid.NewGuid());
Directory.CreateDirectory(root);
var now = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
var calls = new List<string>();
var updating = false;
Task<string> Execute(string action, CancellationToken token) { calls.Add(action); return Task.FromResult(action + " accepted"); }
async Task<bool> Maintenance(Func<Task> action, CancellationToken token) { if (updating) return false; await action(); return true; }
RestartScheduler Scheduler(string name = "normal", DateTimeOffset? start = null) => new(Path.Combine(root,name),Execute,Maintenance,_=>{},TimeZoneInfo.Utc,start??now);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
try
{
    await UpdateMonitorChecks.Run(root);
    await IndependentSchedulesChecks.Run(root);
    await TemperatureChecks.Run(root);
    var s = Scheduler();
    Check(!(await s.ReadAsync()).Settings.Enabled,"default disabled");
    var interval = new RestartScheduleSettings { Enabled=true,Mode="interval",IntervalHours=1 };
    await s.ConfigureAsync(interval,false,now);
    await s.TickAsync(now.AddMinutes(59)); Check(calls.Count==0,"not early");
    await s.TickAsync(now.AddHours(1)); Check(calls.SequenceEqual(new[]{"viewer"}),"viewer due");
    await s.TickAsync(now.AddHours(1)); Check(calls.Count==1,"no duplicate");
    var restored = Scheduler(start:now.AddHours(1));
    Check((await restored.ReadAsync()).NextRun==now.AddHours(2),"future occurrence persists");
    await restored.SkipAsync(now.AddHours(1));
    Check((await restored.ReadAsync()).NextRun==now.AddHours(3),"skip advances interval");
    var missed = Scheduler(start:now.AddHours(5));
    await missed.TickAsync(now.AddHours(5)); Check(calls.Count==1,"startup skips missed run");
    await missed.TickAsync(now.AddHours(9)); Check(calls.Count==1,"resume skips missed run");
    try { await s.ConfigureAsync(interval with {Action="host"},false,now); throw new Exception("missing acknowledgement accepted"); }
    catch(ArgumentException) { }
    await s.ConfigureAsync(interval with {Action="host"},true,now);
    await s.TickAsync(now.AddHours(1)); Check(calls.Count==1 && (await s.ReadAsync()).PendingUntil==now.AddHours(1).AddSeconds(60),"host countdown");
    await s.TickAsync(now.AddHours(1).AddSeconds(59)); Check(calls.Count==1,"countdown respected");
    await s.SkipAsync(now.AddHours(1).AddSeconds(59));
    await s.TickAsync(now.AddHours(1).AddSeconds(60)); Check(calls.Count==1,"cancel prevents host restart");
    await s.ConfigureAsync(interval with {Action="host"},true,now);
    await s.TickAsync(now.AddHours(1));
    var interrupted = Scheduler(start:now.AddHours(1).AddSeconds(10));
    Check((await interrupted.ReadAsync()).PendingUntil is null,"startup cancels pending host reboot");
    updating=true;
    await s.TickAsync(now.AddHours(1).AddSeconds(60)); Check(calls.Count==1 && (await s.ReadAsync()).PendingUntil is null,"update cancels countdown");
    updating=false;
    await s.TickAsync(now.AddHours(1).AddSeconds(120)); Check(calls.Count==1,"fresh countdown after update");
    await s.TickAsync(now.AddHours(1).AddSeconds(180)); Check(calls.Last()=="host" && calls.Count==2,"host executes after countdown");
    await s.ConfigureAsync(interval with {Enabled=false},false,now);
    await s.TickAsync(now.AddDays(5)); Check(calls.Count==2,"disable works");
    var weekly = new RestartScheduleSettings {Enabled=true,Days=[0],Time="03:00"};
    Check(weekly.NextAfter(now,TimeZoneInfo.Utc)==now.AddDays(1).AddHours(3),"weekday time");
    var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
    var spring = new DateTimeOffset(2026,3,8,8,0,0,TimeSpan.Zero);
    Check((weekly with {Time="02:30"}).NextAfter(spring,pacific)==new DateTimeOffset(2026,3,15,9,30,0,TimeSpan.Zero),"skip nonexistent DST time");
    var fall = new DateTimeOffset(2026,11,1,7,0,0,TimeSpan.Zero);
    var once = (weekly with {Time="01:30"}).NextAfter(fall,pacific)!.Value;
    Check((weekly with {Time="01:30"}).NextAfter(once,pacific)>once.AddDays(6),"fall-back occurs once");
    foreach(var invalid in new[]{interval with {IntervalHours=0},weekly with {Days=[]},weekly with {Time="25:00"},weekly with {Action="invalid"}})
    { try {invalid.Validate();throw new Exception("invalid accepted");} catch(ArgumentException){} }
    var bad = Path.Combine(root,"bad"); Directory.CreateDirectory(bad); File.WriteAllText(Path.Combine(bad,"restart-schedule.json"),"broken");
    Check(!(await Scheduler("bad").ReadAsync()).Settings.Enabled,"corrupt state fails closed");
    using var failed = new RestartScheduler(Path.Combine(root,"failed"),(_,_)=>throw new IOException(),Maintenance,_=>{},TimeZoneInfo.Utc,now);
    await failed.ConfigureAsync(interval,false,now); await failed.TickAsync(now.AddHours(1));
    Check((await failed.ReadAsync()).Result.Contains("failed") && (await failed.ReadAsync()).NextRun>now.AddHours(1),"failed action consumed and reported");
    using var updates = new UpdateService(root,new RollingFileLogger(Path.Combine(root,"logs")));
    var updateDir=Path.Combine(root,"updates"); Directory.CreateDirectory(updateDir);
    File.WriteAllText(Path.Combine(updateDir,"progress-test.json"),"{\"state\":\"working\"}");
    Check(!await updates.TryRunMaintenanceAsync(()=>throw new Exception("must not run"),default),"real update status blocks");
    File.WriteAllText(Path.Combine(updateDir,"progress-test.json"),"{\"state\":\"complete\"}");
    Check(await updates.TryRunMaintenanceAsync(()=>Task.CompletedTask,default),"completed update permits maintenance");
    File.WriteAllText(Path.Combine(updateDir,"progress-test.json"),"{");
    Check(!await updates.TryRunMaintenanceAsync(()=>throw new Exception("must not run"),default),"malformed update status blocks");
    Console.WriteLine("Restart scheduling checks passed: due times, persistence, skip, startup/resume, countdown/cancel, update exclusion, DST, validation, failures. No real restart commands executed.");
}
finally { Directory.Delete(root,true); }
