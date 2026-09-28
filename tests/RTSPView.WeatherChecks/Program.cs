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
        SystemWidgetChecks.Run();
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
        foreach (var font in new[] { 12, 24, 64 })
        {
            var options = new WeatherOptions { Preset = "minimal", FontSize = font, Fields = ["temperature", "condition", "highLow"] };
            var bounds = WeatherGeometry.Bounds(new() { Weather = options, X = 100, Y = 100 }, 640, 360);
            compact.Width = bounds.Width; compact.Height = bounds.Height;
            compact.Update(options, data); compact.Measure(new Size(bounds.Width, bounds.Height));
            compact.Arrange(new Rect(0, 0, bounds.Width, bounds.Height)); compact.UpdateLayout();
            Check(FindText(compact).Any(t => t.Text.StartsWith("H ")), "Minimal retains selected high/low at font " + font);
            Check(bounds.Left + bounds.Width <= 640 && bounds.Top + bounds.Height <= 360, "weather geometry stays in camera bounds");
        }
        foreach(var size in new[]{new Size(180,100),new Size(300,180),new Size(640,360),new Size(360,640)})
        {
            detailed.Width=size.Width;detailed.Height=size.Height;detailed.Measure(size);detailed.Arrange(new Rect(size));detailed.UpdateLayout();
            Check(detailed.DesiredSize.Width<=size.Width+24,"native resize remains bounded "+size);
        }
        foreach(var alignment in new[] {"left","center","right"})
        foreach(var size in new[] {new Size(180,160),new Size(640,360),new Size(1280,720)})
        {
            var aligned=new WeatherView {Width=size.Width,Height=size.Height};
            var settings=new WeatherOptions {Alignment=alignment,Fields=["temperature","condition","highLow"]};
            aligned.Update(settings,data);aligned.Measure(size);aligned.Arrange(new Rect(size));aligned.UpdateLayout();
            Check(FindText(aligned).All(t=>t.TextAlignment.ToString().ToLowerInvariant()==alignment),"all weather text respects "+alignment+" at "+size);
            foreach(var text in FindText(aligned).Where(t=>t.Opacity>0&&t.ActualHeight>0))
            {
                var bounds=text.TransformToAncestor(aligned).TransformBounds(new Rect(text.RenderSize));
                Check(bounds.Left>=-.5&&bounds.Right<=size.Width+.5&&bounds.Top>=-.5&&bounds.Bottom<=size.Height+.5,"aligned weather text stays inside card");
            }
            Check(JsonSerializer.Deserialize<WeatherOptions>(JsonSerializer.Serialize(settings))!.Alignment==alignment,"weather alignment survives save/reload");
        }
        compact.IndependentWidget = true;
        var matrix = new Canvas { Width = 1100, Height = 900, Background = Brushes.DimGray };
        var cases = new[] { new Size(180,160), new Size(320,180), new Size(530,350), new Size(180,400), new Size(640,120), new Size(160,96) };
        for (var index=0; index<cases.Length; index++)
        {
            var box=cases[index];
            var options=new WeatherOptions { Preset="minimal", Fields=["temperature","condition","highLow"], FontSize=24, IconSize=36 };
            var widget=new WallWidget { Kind="weather", Weather=options, HeightPercent=20 };
            var scale=widget.ContentScaleFor(box.Width,box.Height);
            var card=new WeatherView { IndependentWidget=true, Width=box.Width/scale, Height=box.Height/scale, LayoutTransform=new ScaleTransform(scale,scale) };
            card.Update(options,data with {Temperature=index%3==0?2.77777778:index%3==1?-24.44444444:40.55555556});
            Canvas.SetLeft(card,index<3?new[]{10d,200,540}[index]:index==3?10:200);Canvas.SetTop(card,index<3?10:index==3?380:index==4?380:520);
            matrix.Children.Add(card);
            matrix.Measure(new Size(1100,900));matrix.Arrange(new Rect(0,0,1100,900));matrix.UpdateLayout();
            var secondary=FindText(card).Single(t=>t.Text.StartsWith("H "));
            Check(secondary.FontSize>=12 && secondary.Opacity > 0 && secondary.ActualHeight > 0,"responsive high/low remains readable and visible "+box);
            var main=FindText(card).Single(t=>t.Text is "37°F" or "-12°F" or "105°F");
            var transformed=main.TransformToAncestor(card).TransformBounds(new Rect(main.RenderSize));
            Check(transformed.Left>=-.5&&transformed.Right<=card.ActualWidth+.5&&transformed.Top>=-.5&&transformed.Bottom<=card.ActualHeight+.5,"complete independent temperature stays inside "+box);
        }
        matrix.Measure(new Size(1100,900));matrix.Arrange(new Rect(0,0,1100,900));matrix.UpdateLayout();
        var widgetBitmap=new RenderTargetBitmap(1100,900,96,96,PixelFormats.Pbgra32);widgetBitmap.Render(matrix);
        using(var file=File.Create("artifacts/weather/widget-layout-matrix.png")){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(widgetBitmap));encoder.Save(file);}
        compact.Update(new() {Location="Not fetched"},null);
        Check(FindText(compact).Any(t=>t.Text=="Waiting for weather"),"native no-fetch state is explicit");
        Check(WeatherFormatting.Icon(0,false)=="☾" && WeatherFormatting.Icon(0,true)=="☀","native day/night icons");
        detailed.Update(new() {Fields=WeatherOptions.AllowedFields},data);
        detailed.Width=180;detailed.Height=100;detailed.Measure(new Size(180,100));detailed.Arrange(new Rect(0,0,180,100));detailed.UpdateLayout();
        Check(FindText(detailed).Where(t=>t.Opacity>0).All(t=>t.FontSize>=10),"adaptive small cards keep readable type instead of warning clutter");
        Directory.CreateDirectory("artifacts/widgets");
        var contracts = (from size in new[] {new Size(160,96),new Size(180,400),new Size(640,120),new Size(320,180),new Size(640,360),new Size(960,540),new Size(1920,1080)}
                         from density in new[] {"auto","minimal","standard","detailed"}
                         select new { Density=density, Viewport=WidgetViewport.For(size.Width,size.Height,density), Weather=WidgetViewport.WeatherFor(size.Width,size.Height,density), Photo=AircraftPhotoViewport.For(size.Width,size.Height), PhotoEmphasis=AircraftPhotoViewport.For(size.Width,size.Height,true), PhotoPair=AircraftPhotoViewport.For(size.Width,size.Height,false,true) }).ToArray();
        File.WriteAllText("artifacts/widgets/viewport-contract.json",JsonSerializer.Serialize(contracts));
        var large = new WeatherView { Width=1280, Height=720 };
        large.Update(new() { Location="Test city", Fields=["location","temperature","condition","highLow"] }, data);
        large.Measure(new Size(1280,720));large.Arrange(new Rect(0,0,1280,720));large.UpdateLayout();
        var temperature=FindText(large).Single(t=>t.Text=="72°F");
        var highLow=FindText(large).Single(t=>t.Text.StartsWith("H "));
        Check(temperature.Opacity>0 && temperature.ActualHeight>0 && highLow.Opacity>0 && highLow.ActualHeight>0,"large sparse card retains both core readings");
        Check(temperature.TransformToAncestor(large).Transform(new Point()).X<640 && highLow.TransformToAncestor(large).Transform(new Point()).X>640,"large sparse card distributes readings across both halves");
        Check(temperature.FontSize>=190 && highLow.FontSize>=50,"large card scales primary and secondary type");
        var largeBitmap=new RenderTargetBitmap(1280,720,96,96,PixelFormats.Pbgra32);largeBitmap.Render(large);
        using(var file=File.Create("artifacts/weather/large-weather-composition.png")){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(largeBitmap));encoder.Save(file);}
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
            var settings=await store.LoadAsync();Check(settings.SchemaVersion==AppSettings.CurrentSchemaVersion&&settings.WeatherOverlays.Count==0,"stable settings migrate with weather off");
            var weather=new WeatherOptions{Location="Seattle",Latitude=47.6062,Longitude=-122.3321};
            var layout=settings.Layouts[0] with { Tiles=settings.Layouts[0].Tiles.Take(8).Append(new(){Kind="weather",ItemId="weather-1",Row=2,Column=2,Weather=weather}).ToArray() };
            settings=settings with { Layouts=[layout],WeatherOverlays=[new(){HostCameraSlot=1,Weather=weather}] };
            Check(WeatherConfiguration.OnlyPresentationChanged(settings, settings with {WeatherOverlays=[]}),"overlay changes use isolated presentation path");
            Check(!WeatherConfiguration.OnlyPresentationChanged(settings, settings with {StartFullScreen=false}),"display changes still use normal settings path");
            await store.SaveAsync(settings);var loaded=await store.LoadAsync();Check(loaded.Layouts[0].Tiles.Last().Kind=="weather"&&loaded.WeatherOverlays.Count==1,"eight cameras plus weather and overlay persist");
            Check(File.Exists(path+".before-weather.json"),"pre-weather rollback backup retained");
            Check(JsonSettingsStore.ParseImport(JsonSerializer.Serialize(loaded)).Layouts[0].Tiles.Last().Weather!.CacheKey==weather.CacheKey,"weather survives export/import");
            var legacy = new WallWidget { Id="legacy", Weather=weather with { FontSize=64, Padding=40 }, WidthPercent=35, HeightPercent=30, X=80, Y=60 };
            var legacySettings = settings with { SchemaVersion=20, Layouts=[settings.Layouts[0] with { Widgets=[legacy] }] };
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(legacySettings));
            var upgraded=await store.LoadAsync();
            Check(upgraded.SchemaVersion==AppSettings.CurrentSchemaVersion && upgraded.Layouts[0].Widgets[0].Cell is null && upgraded.Layouts[0].Widgets[0].Weather!.Density=="auto","schema 20 keeps floating placement and defaults to responsive Auto");
            Check(upgraded.Layouts[0].Widgets[0].Bounds(1920,1080)==legacy.Bounds(1920,1080),"legacy geometry survives responsive migration exactly");
            await store.SaveAsync(upgraded);
            Check(File.Exists(path+".before-responsive-widgets.json"),"responsive migration retains rollback backup");
            foreach(var gridSize in new[]{1,2,3})
            {
                var gridLayout = new WallLayout { Rows=gridSize, Columns=gridSize, Tiles=[], Widgets=[legacy with {Cell=new(){Row=gridSize-1,Column=gridSize-1}}] };
                WallLayout.Validate([gridLayout],gridLayout.Id);
                var bounds=gridLayout.Widgets[0].Bounds(gridLayout);
                Check(Math.Abs(bounds.Width-1920d/gridSize)<.001 && Math.Abs(bounds.Height-1080d/gridSize)<.001 && bounds.Left+bounds.Width<=1920.001,"grid widget follows "+gridSize+" by "+gridSize+" layout");
                var roundTrip=JsonSettingsStore.ParseImport(JsonSerializer.Serialize(upgraded with {Layouts=[gridLayout]}));
                Check(roundTrip.Layouts[0].Widgets[0].Cell==gridLayout.Widgets[0].Cell,"grid placement survives export/import");
            }
            var mixed=new WallLayout { Rows=2, Columns=3, Tiles=[], RowWeights=[.25,.75],ColumnWeights=[.2,.3,.5] };
            var spanning=legacy with { Cell=new(){Row=0,Column=1,RowSpan=2,ColumnSpan=2} };
            var span=spanning.Bounds(mixed);Check(span.Width==1536 && span.Height==1080 && span.Left==384,"multi-cell widget follows unequal grid proportions");
            Check(spanning.Bounds(mixed with {Rows=1,Columns=1,RowWeights=[],ColumnWeights=[]}).Width==1920,"shrinking grid retains and clamps widget");
            settings=settings with { StorageRevision=(await store.LoadAsync()).StorageRevision };
            await store.SaveAsync(settings);
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
