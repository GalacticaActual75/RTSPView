using System.Text.Json;
using RTSPView.Controller;
using RTSPView.Core;

internal static class UpdateMonitorChecks
{
    public static async Task Run(string root)
    {
        var directory=Path.Combine(root,"daily-updates");
        var now=new DateTimeOffset(2026,9,13,0,0,0,TimeSpan.Zero);
        var calls=0;var installs=0;var channel="beta";var failed=false;var version="1.0.40-beta.4";
        UpdateStatus Initial()=>new(version,null,false,false,"Pending","https://github.com/example/test/releases","beta",channel);
        Task<UpdateStatus> Check(CancellationToken token){calls++;return Task.FromResult(Initial() with {LatestVersion="1.0.40-beta.5",ChannelAvailable=!failed,UpdateAvailable=!failed,CheckFailed=failed,Message=failed?"Unavailable":"Update available"});}
        Task<UpdateLaunchResult> Install(string c,string v,CancellationToken token){installs++;return Task.FromResult(new UpdateLaunchResult(true,"Started"));}
        UpdateMonitor Monitor()=>new(directory,Initial,Check,Install,_=>{});
        void Assert(bool result,string message){if(!result)throw new Exception(message);}
        using var monitor=Monitor();
        await monitor.StatusAsync();Assert(calls==0,"reading cached status never calls GitHub");
        var first=await monitor.CheckAsync(false,now);Assert(calls==1&&first.NextCheck==now.AddHours(24),"daily timer");
        await monitor.CheckAsync(false,now.AddHours(23));Assert(calls==1,"no early automatic request");
        await monitor.CheckAsync(true,now.AddSeconds(10));Assert(calls==1,"manual click cooldown");
        Assert(UpdateMonitor.Notice(first).Visible,"newer beta visible");
        await monitor.NotificationsAsync(false);
        Assert(!JsonSerializer.Deserialize<WallUpdateNotice>(File.ReadAllText(Path.Combine(directory,"update-notice.json")))!.Visible,"toggle hides badge");
        using var restarted=Monitor();
        await restarted.CheckAsync(false,now.AddHours(2));Assert(calls==1,"restart retains daily cache");
        Assert(!(await restarted.StatusAsync()).ShowWallNotifications,"notification preference persists");
        await restarted.CheckAsync(false,now.AddHours(24));Assert(calls==2,"checks continue with notifications disabled");
        await restarted.NotificationsAsync(true);
        Assert(!(await restarted.InstallFromWallAsync(new("1.0.40-beta.5","beta",false),default)).Started&&installs==0,"confirmation required");
        Assert(!(await restarted.InstallFromWallAsync(new("1.0.40-beta.3","beta",true),default)).Started&&installs==0,"stale version rejected");
        Assert(!(await restarted.InstallFromWallAsync(new("1.0.40-beta.5","stable",true),default)).Started&&installs==0,"wrong channel rejected");
        Assert((await restarted.InstallFromWallAsync(new("1.0.40-beta.5","beta",true),default)).Started&&installs==1,"confirmed current update delegates installer");
        Assert(!(await restarted.InstallFromWallAsync(new("1.0.40-beta.5","beta",true),default)).Started&&installs==1,"duplicate install rejected");
        failed=true;var failure=await restarted.CheckAsync(false,now.AddHours(48));
        Assert(failure.NextCheck==now.AddHours(49),"initial failure backoff");
        failure=await restarted.CheckAsync(false,now.AddHours(49));Assert(failure.NextCheck==now.AddHours(51),"exponential backoff");
        version="1.0.40-beta.5";Assert(!(await restarted.StatusAsync()).UpdateAvailable,"installed version invalidates old notice");
        Assert(!UpdateMonitor.Notice(first with {LatestVersion="1.0.39",SelectedChannel="stable"}).Visible,"no downgrade badge");
        Assert(UpdateMonitor.Notice(first with {LatestVersion="1.0.40",SelectedChannel="stable"}).Visible,"stable graduation is newer than same beta");
        Console.WriteLine("PASS daily update cache, restart persistence, manual cooldown, notification toggle, backoff, version ordering and confirmed wall installation (fake installer).");
    }
}
