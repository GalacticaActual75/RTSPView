using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RTSPView.Core;
using RTSPView.Infrastructure;
using RTSPView.Controller;
using RTSPView.Viewer;

internal static class Program
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--live"))
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var live = new OpenMeteoProvider(client).FetchAsync(new() { Location = "Seattle", Latitude = 47.6062, Longitude = -122.3321 }, CancellationToken.None).GetAwaiter().GetResult();
            Check(live.Temperature.HasValue && live.Hourly.Length > 0 && live.Daily.Length == 7 && live.TimeZone == "America/Los_Angeles", "live Open-Meteo response parses current, hourly, daily and timezone");
        }
        DataChecks().GetAwaiter().GetResult();
        var app = new Application();
        var now = DateTimeOffset.UtcNow;
        var data = new WeatherSnapshot { Key = "47.6062,-122.3321", Temperature = 22.2, Code = 2, FetchedAt = now, ValidAt = now, TimeZone = "America/Los_Angeles", Humidity = 48, Wind = 2.7,
            Daily = [new(WeatherFormatting.LocalTime(now,"America/Los_Angeles").ToString("yyyy-MM-dd"),24.4,12.2,2,null,null)],
            Hourly = Enumerable.Range(1,6).Select(i=>new WeatherHour(now.AddHours(i),22+i/2d,2,10)).ToArray() };
        var wall = new Grid { Width=960, Height=540, Background=Brushes.Black };
        wall.ColumnDefinitions.Add(new());wall.ColumnDefinitions.Add(new());
        var compact = new WeatherView { Width=300, Height=180, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center };
        compact.Update(new() { Location="Seattle", Latitude=47.6062, Longitude=-122.3321 },data);wall.Children.Add(compact);
        var detailed=new WeatherView { Margin=new(12) };Grid.SetColumn(detailed,1);wall.Children.Add(detailed);
        detailed.Update(new() { Location="Seattle", Preset="dashboard", Fields=WeatherOptions.AllowedFields },data);
        wall.Measure(new Size(960,540));wall.Arrange(new Rect(0,0,960,540));wall.UpdateLayout();
        Check(FindText(compact).Any(t=>t.Text=="72°F"),"native temperature conversion renders");
        Check(FindText(compact).Any(t=>t.Text.Contains("Open-Meteo")),"native attribution renders");
        Check(FindText(detailed).Any(t=>t.Text.Contains("Humidity")),"large native tile exposes metrics");
        var bitmap=new RenderTargetBitmap(960,540,96,96,PixelFormats.Pbgra32);bitmap.Render(wall);
        Directory.CreateDirectory("artifacts/weather");using(var file=File.Create("artifacts/weather/native-weather.png")){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(file);}
        compact.Update(new() { Location="Offline test" },data with { FetchedAt=now.AddDays(-1), ValidAt=now.AddDays(-1) });
        Check(FindText(compact).Any(t=>t.Text=="Weather unavailable"),"expired native current weather is hidden");
        compact.Update(new() { Location="Stale test" }, data with { FetchedAt=now.AddHours(-2), ValidAt=now.AddHours(-2) });
        Check(FindText(compact).Any(t=>t.Text.StartsWith("Outdated")),"small native cards retain stale warning");
        foreach(var size in new[]{new Size(180,100),new Size(300,180),new Size(640,360),new Size(360,640)})
        {
            detailed.Width=size.Width;detailed.Height=size.Height;detailed.Measure(size);detailed.Arrange(new Rect(size));detailed.UpdateLayout();
            Check(detailed.DesiredSize.Width<=size.Width+24,"native resize remains bounded "+size);
        }
        app.Shutdown();
    }
    private static IEnumerable<TextBlock> FindText(DependencyObject node)
    {
        if(node is TextBlock text)yield return text;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in FindText(VisualTreeHelper.GetChild(node,i)))yield return child;
    }
    private static async Task DataChecks()
    {
        var now=DateTimeOffset.FromUnixTimeSeconds(1789992000);
        var json="""{"timezone":"America/Los_Angeles","current":{"time":1789992000,"temperature_2m":22.2,"relative_humidity_2m":null,"weather_code":123},"hourly":{"time":[1789992000,1789995600],"temperature_2m":[22.2,null],"weather_code":[2,3],"precipitation_probability":[0,null]},"daily":{"time":[1789974000],"temperature_2m_max":[24.4],"temperature_2m_min":[12.2],"weather_code":[2],"sunrise":[null],"sunset":[null]}}""";
        var snapshot=OpenMeteoProvider.Parse(json,"47.6062,-122.3321",now);
        Check(snapshot.Humidity is null&&snapshot.Hourly[1].Temperature is null,"missing provider measurements stay missing");
        Check(snapshot.Daily[0].Sunrise is null,"polar or missing solar times stay missing");
        Check(WeatherFormatting.Condition(snapshot.Code)=="Conditions unavailable","unknown weather codes are safe");
        Check(snapshot.Freshness(now)=="fresh"&&snapshot.Freshness(now.AddHours(1))=="stale"&&snapshot.Freshness(now.AddHours(7))=="unavailable","freshness uses real data age");
        Check(WeatherFormatting.LocalTime(DateTimeOffset.Parse("2026-03-08T09:30:00Z"),"America/Los_Angeles").Hour==1&&WeatherFormatting.LocalTime(DateTimeOffset.Parse("2026-03-08T10:30:00Z"),"America/Los_Angeles").Hour==3,"location timezone follows DST");
        var directory=Path.Combine(Path.GetTempPath(),"RTSPView-weather-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"settings.json");var store=new JsonSettingsStore(path);
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new AppSettings{SchemaVersion=15}));
            var settings=await store.LoadAsync();Check(settings.SchemaVersion==16&&settings.WeatherOverlays.Count==0,"stable settings migrate with weather off");
            var weather=new WeatherOptions{Location="Seattle",Latitude=47.6062,Longitude=-122.3321};
            var layout=settings.Layouts[0] with { Tiles=settings.Layouts[0].Tiles.Take(8).Append(new(){Kind="weather",ItemId="weather-1",Row=2,Column=2,Weather=weather}).ToArray() };
            settings=settings with { Layouts=[layout],WeatherOverlays=[new(){HostCameraSlot=1,Weather=weather}] };
            Check(WeatherConfiguration.OnlyPresentationChanged(settings, settings with {WeatherOverlays=[]}),"overlay changes use isolated presentation path");
            Check(!WeatherConfiguration.OnlyPresentationChanged(settings, settings with {StartFullScreen=false}),"display changes still use normal settings path");
            await store.SaveAsync(settings);var loaded=await store.LoadAsync();Check(loaded.Layouts[0].Tiles.Last().Kind=="weather"&&loaded.WeatherOverlays.Count==1,"eight cameras plus weather and overlay persist");
            Check(File.Exists(path+".before-weather.json"),"pre-weather rollback backup retained");
            Check(JsonSettingsStore.ParseImport(JsonSerializer.Serialize(loaded)).Layouts[0].Tiles.Last().Weather!.CacheKey==weather.CacheKey,"weather survives export/import");
            try{WallLayout.Validate([layout with {Tiles=layout.Tiles.Append(layout.Tiles.Last()).ToArray()}],layout.Id);throw new Exception("duplicate accepted");}catch(InvalidDataException){Console.WriteLine("PASS duplicate weather item rejected");}
            try{(weather with {Latitude=double.NaN}).Validate();throw new Exception("coordinate accepted");}catch(InvalidDataException){Console.WriteLine("PASS nonfinite coordinate rejected");}
            await WeatherCache.WriteAsync(directory,[snapshot],CancellationToken.None);Check((await WeatherCache.ReadAsync(directory)).Single().Temperature==22.2,"cache round trip");
            await File.WriteAllTextAsync(WeatherCache.PathFor(directory),"broken");Check((await WeatherCache.ReadAsync(directory)).Length==0,"bad weather cache does not affect settings");
            var provider=new FakeProvider();using var service=new WeatherService(directory,provider);await service.StartAsync(CancellationToken.None);
            for(var i=0;i<100&&service.Snapshots.Length==0;i++)await Task.Delay(20);
            Check(service.Snapshots.Length==1&&provider.Calls==1,"tile and overlay share one provider request");
            await service.StopAsync(CancellationToken.None);
            Check((await store.LoadAsync()).Cameras.SequenceEqual(settings.Cameras),"weather refresh leaves camera settings untouched");
            Check(StreamCatalog.DeleteCamera(settings, 1).WeatherOverlays.Count == 0,"deleting a host removes its weather overlay");
        }
        finally{Directory.Delete(directory,true);}
    }
    private sealed class FakeProvider:IWeatherProvider
    {
        public int Calls;
        public Task<WeatherSnapshot> FetchAsync(WeatherOptions location,CancellationToken token){Calls++;return Task.FromResult(new WeatherSnapshot{Key=location.CacheKey,FetchedAt=DateTimeOffset.UtcNow,ValidAt=DateTimeOffset.UtcNow,Temperature=22});}
    }
}
