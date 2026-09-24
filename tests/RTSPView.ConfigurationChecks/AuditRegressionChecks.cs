using RTSPView.Core;
using RTSPView.Infrastructure;
internal static class AuditRegressionChecks
{
    public static async Task Run(string root)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var store = new JsonSettingsStore(Path.Combine(root,"concurrent.json"));
        await store.SaveAsync(new AppSettings());
        var native = await store.LoadAsync();
        var web = await store.LoadAsync();
        await store.SaveAsync(web with {ShowCameraNames = false});
        var draft = native with { Cameras = native.Cameras.Select(c => c.Slot == 1 ? c with {Name="Native edit"} : c).ToArray() };
        var merged = await store.SaveCameraEditsAsync(native,draft);
        Check(!merged.ShowCameraNames && merged.Cameras[0].Name == "Native edit", "Native save must retain newer web display edits");
        try { await store.SaveAsync(web); throw new Exception("Stale web write accepted"); } catch(InvalidDataException) { }
        try { await store.SaveCameraEditsAsync(native,draft); throw new Exception("Same-camera conflict accepted"); } catch(InvalidDataException) { }
        try { await store.SaveCameraEditsAsync(native,draft,true); throw new Exception("Stale import accepted"); } catch(InvalidDataException) { }
        var originals = await Task.WhenAll(store.LoadAsync(),store.LoadAsync());
        var results = await Task.WhenAll(originals.Select(async (s,i) => { try { await store.SaveAsync(s with {PreferredMonitor=i+1});return true; } catch(InvalidDataException){return false;} }));
        Check(results.Count(r=>r)==1,"Only one concurrent whole-settings writer may win");
        foreach(var cap in new[]{5,30,60,300}) {
            var camera=new CameraSettings {MaximumReconnectBackoffSeconds=cap};
            var delays=Enumerable.Range(1,16).Select(camera.ReconnectDelaySeconds).ToArray();
            Check(delays[0]==1 && delays[^1]==cap && delays.All(d=>d<=cap),"Reconnect reaches its configured cap");
            Check(delays.Zip(delays.Skip(1)).All(p=>p.Second>=p.First),"Reconnect delay is monotonic");
        }
        var baseSettings=new AppSettings().Normalize();
        var weather=new WeatherOptions {Location="Test",Latitude=40,Longitude=-74};
        var withWeather=baseSettings with {WeatherOverlays=[new() {HostCameraSlot=1,Weather=weather}]};
        Check(AutomationConfiguration.Hash(baseSettings)==AutomationConfiguration.Hash(withWeather),"Camera weather must not invalidate automation");
        Check(AutomationConfiguration.Hash(baseSettings)!=AutomationConfiguration.Hash(baseSettings with {Cameras=baseSettings.Cameras.Select(c=>c with {RtspUrl="rtsp://test/stream"}).ToArray()}),"Stream changes must invalidate automation");
        Console.WriteLine("PASS stale writes, native merge, conflicting imports, concurrent writers, retry caps and weather isolation");
    }
}
