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
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); Console.WriteLine("PASS " + message); }
    [STAThread]
    private static void Main(string[] args)
    {
        // Explicit opt-in diagnostic; normal regression tests never contact flight services.
        if (args.Length == 4 && args[0] == "--provider-smoke")
        {
            ProviderSmoke(args).GetAwaiter().GetResult(); return;
        }
        if (!args.Contains("--presentation-only")) Backend().GetAwaiter().GetResult();
        var colored = new AircraftOptions { BackgroundColor = "#123456", BackgroundOpacity = 50 };
        var surface = new Border(); WidgetAppearance.Apply(surface, colored.Appearance);
        Check(((SolidColorBrush)surface.Background).Color == Color.FromArgb(127,18,52,86), "custom widget background retains opacity in Live View");
        var now = DateTimeOffset.UtcNow;
        var rotation = new AircraftRotation();
        var ranked = Enumerable.Range(0,5).Select(i => new AircraftTrack { Hex = i.ToString() }).ToArray();
        string Pair(AircraftTrack[] tracks, int seconds) => string.Join(",", rotation.Select(tracks, 5, now.AddSeconds(seconds)).Select(a => a.Hex));
        Check(Pair(ranked,0)=="0,1", "legacy five-flight setting capped at two");
        Check(Pair(ranked.Reverse().ToArray(),19)=="0,1", "refresh retains selected pair");
        Check(Pair(ranked,20)=="2,3" && Pair(ranked,40)=="4,0" && Pair(ranked,60)=="0,1", "nearest ranked pairs rotate every 20 seconds");
        Check(Pair(ranked.Skip(1).ToArray(),61)=="1,2", "departed aircraft replaced immediately");
        Check(Pair([],62)=="" && Pair(ranked,63)=="0,1", "empty traffic resets rotation");
        var options = new AircraftOptions { Location = "Seattle", Latitude = 47.6062, Longitude = -122.3321 };
        var snapshot = new AircraftSnapshot { Key = options.CacheKey, FetchedAt = now, Aircraft = [Track(now), Track(now) with { Hex = "b12345", Callsign = "ASA456", Type = "B39M", AltitudeFeet = 18200 }] };
        var root = new Grid { Width = 1100, Height = 620, Background = System.Windows.Media.Brushes.Black };
        root.ColumnDefinitions.Add(new()); root.ColumnDefinitions.Add(new());
        var weather = new WeatherView { Width = 370, Height = 230, VerticalAlignment = VerticalAlignment.Center };
        weather.Update(new() { Location = "Seattle" }, new() { FetchedAt = now, ValidAt = now, Temperature = 22, Code = 2 }); root.Children.Add(weather);
        var aircraft = new AircraftView { Margin = new(16) }; Grid.SetColumn(aircraft, 1); root.Children.Add(aircraft); aircraft.Update(options with { Preset = "board" }, snapshot);
        root.Measure(new System.Windows.Size(1100, 620)); root.Arrange(new Rect(0, 0, 1100, 620)); root.UpdateLayout();
        Check(((SolidColorBrush)weather.Background).Color == ((SolidColorBrush)aircraft.Background).Color, "weather and aircraft share surface styling");
        var testPhoto = new AircraftPhoto("https://t.plnspttrs.net/layout-test.jpg", "https://www.planespotters.net/photo/1", "Layout fixture");
        var imageCache = AircraftPhotoImages.Cache.Tasks;
        var testImage = BitmapSource.Create(3,2,96,96,PixelFormats.Bgr32,null,new byte[24],12); testImage.Freeze();
        imageCache[testPhoto.Url] = Task.FromResult<BitmapSource?>(testImage);
        var cacheTime = now; var attempts = 0;
        var retryCache = new AircraftImageCache(_ => Task.FromResult<BitmapSource?>(++attempts == 1 ? null : testImage), () => cacheTime);
        Check(retryCache.Get(testPhoto).GetAwaiter().GetResult() is null && retryCache.Get(testPhoto).GetAwaiter().GetResult() is null && attempts == 1, "failed native image uses retry cooldown");
        cacheTime = cacheTime.AddSeconds(61);
        Check(retryCache.Get(testPhoto).GetAwaiter().GetResult() == testImage && attempts == 2, "native image recovers after transient failure without restart");
        var failedPhoto = testPhoto with { Url = "https://t.plnspttrs.net/failed.jpg" };
        imageCache[failedPhoto.Url] = Task.FromResult<BitmapSource?>(null);
        var pendingPhoto = testPhoto with { Url = "https://t.plnspttrs.net/pending.jpg" };
        var pending = new TaskCompletionSource<BitmapSource?>(); imageCache[pendingPhoto.Url] = pending.Task;
        bool Visible(UIElement element, DependencyObject parent)
        {
            for (DependencyObject? n = element; n != null && n != parent; n = VisualTreeHelper.GetParent(n))
                if (n is UIElement e && (e.Opacity == 0 || e.Visibility != Visibility.Visible || e.RenderSize.Height <= 0)) return false;
            return true;
        }
        foreach (var box in new[] { new Size(160,96), new Size(180,400), new Size(640,120), new Size(320,180), new Size(640,360), new Size(960,540), new Size(1920,1080) })
        foreach (var photo in new AircraftPhoto?[] { null, failedPhoto, pendingPhoto, testPhoto })
        {
            var card = new AircraftView { Width=box.Width, Height=box.Height };
            var data = snapshot with { Aircraft=snapshot.Aircraft.Select(t=>t with { Photo=photo, RegisteredOwner="A very long aircraft owner organization with international operations and charter services", Destination="SEA · Seattle Tacoma International Airport" }).ToArray() };
            card.Update(options with { Preset="board", TextSizes=new() { ["owner"]=64, ["altitude"]=8 } }, data);
            card.Measure(box);card.Arrange(new Rect(box));card.UpdateLayout();
            var text=VisualTexts(card).Where(t=>Visible(t,card)).ToArray();
            Check(text.Any(t=>t.Text=="UAL123") && text.Any(t=>t.Text.StartsWith("ALT ")), "responsive card retains identifier and altitude " + box);
            foreach(var t in text)
            {
                var bounds=t.TransformToAncestor(card).TransformBounds(new Rect(t.RenderSize));
                Check(bounds.Left>=-.5 && bounds.Top>=-.5 && bounds.Right<=box.Width+.5 && bounds.Bottom<=box.Height+.5 && t.FontSize>=10, "visible native text stays readable and inside " + box);
            }
            if(photo!=testPhoto)Check(!VisualImages(card).Any(), "missing, failed and pending images reserve no native rectangle " + box);
            if(box.Width<600||box.Height<240)Check(!text.Any(t=>t.Text=="ASA456"),"small board rotates one aircraft " + box);
            else Check(text.Any(t=>t.Text=="ASA456"),"large board shows two aircraft " + box);
        }
        aircraft.Width=620;aircraft.Height=420;
        var longCard = new AircraftView { Width=180, Height=160 };
        longCard.Update(options, snapshot with { Aircraft=[snapshot.Aircraft[0] with { Callsign="LONGCALLSIGN123",Type="An unusually long aircraft type description",RegisteredOwner="An unusually long owner name" }] });
        longCard.Measure(new Size(180,160));longCard.Arrange(new Rect(0,0,180,160));longCard.UpdateLayout();
        Check(VisualTexts(longCard).Any(t=>t.Text=="LONGCALLSIGN123"&&Visible(t,longCard)),"long callsign remains visible without shrinking text");
        foreach(var label in VisualTexts(longCard).Where(t=>Visible(t,longCard)))
        {
            var bounds=label.TransformToAncestor(longCard).TransformBounds(new Rect(label.RenderSize));
            Check(bounds.Right<=180.5 && bounds.Bottom<=160.5,"long native text remains inside card");
        }
        aircraft.Update(options with { ShowPhoto=true, Fields=["type","altitude"] }, snapshot with { Aircraft=[snapshot.Aircraft[0] with { Photo=testPhoto }] });root.UpdateLayout();
        Check(VisualImages(aircraft).Any(i=>i.Source is not null && Visible(i,aircraft)),"loaded photo appears with room for its credit");
        var stableTree=aircraft.Child;
        var stableSnapshot=snapshot with { Aircraft=[snapshot.Aircraft[0] with { Photo=testPhoto }] };
        aircraft.Update(options with { ShowPhoto=true, Fields=["type","altitude"] },stableSnapshot);
        Check(ReferenceEquals(stableTree,aircraft.Child),"unchanged polling retains the native visual tree");
        Directory.CreateDirectory("artifacts/aircraft");
        var bitmap = new RenderTargetBitmap(1100,620,96,96,PixelFormats.Pbgra32); bitmap.Render(root);
        using (var file = File.Create("artifacts/aircraft/native-aircraft.png")) { var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(file); }
        aircraft.Update(options with { HideWhenEmpty = true }, snapshot with { Aircraft = [] }, true);
        Check(aircraft.Visibility == Visibility.Hidden, "empty widget honors hide-when-empty preference");
        aircraft.Update(options with { HideWhenEmpty = false }, snapshot with { Aircraft = [] }, true);
        Check(aircraft.Visibility == Visibility.Visible && Texts(aircraft).Contains("No aircraft nearby"), "empty widget stays visible by default");
        aircraft.Update(options with { HideWhenEmpty = true }, snapshot, true);
        Check(aircraft.Visibility == Visibility.Visible, "hidden widget returns when aircraft arrive");
        foreach (var feed in new AircraftSnapshot?[] { null, snapshot with { RefreshFailed = true }, snapshot with { FetchedAt = now.AddSeconds(-40) }, snapshot with { FetchedAt = now.AddMinutes(-2) }, snapshot with { Aircraft = [Track(now) with { Latitude = 0, Longitude = 0 }] } })
        {
            aircraft.Update(options with { HideWhenEmpty = true }, feed, true);
            Check(aircraft.Visibility == Visibility.Hidden, "auto-hide widget stays hidden without fresh matching traffic");
        }
        aircraft.Update(options with { HideWhenEmpty = true, MinimumAltitudeFeet = 50000 }, snapshot, true);
        Check(aircraft.Visibility == Visibility.Hidden, "auto-hide widget honors altitude filters");
        aircraft.Update(options with { HideWhenEmpty = true }, snapshot with { Aircraft = [] });
        Check(aircraft.Visibility == Visibility.Visible, "permanent tile stays visible regardless of widget hiding preference");
        aircraft.Update(options, snapshot with { FetchedAt = now.AddMinutes(-2) }, true); Check(aircraft.Visibility == Visibility.Visible && Texts(aircraft).Contains("Aircraft data unavailable"), "outage remains visible and hides stale aircraft");
        aircraft.Update(options, snapshot with { Aircraft = [] }); Check(Texts(aircraft).Contains("No aircraft nearby"), "empty tile stays visible");
        var fadeOptions = options with { HideWhenEmpty = true, ShowPhoto = false, FadeEnabled = true, FadeInMilliseconds = 100, FadeOutMilliseconds = 200 };
        aircraft.Update(fadeOptions, snapshot, true); root.UpdateLayout(); Pump(150);
        var fadingContent = aircraft.Child;
        aircraft.Update(fadeOptions, snapshot with { Aircraft = [] }, true);
        Check(aircraft.Visibility == Visibility.Visible && ReferenceEquals(fadingContent,aircraft.Child), "native fade-out retains populated card until completion");
        Pump(300); Check(aircraft.Visibility == Visibility.Hidden, "native fade-out completes by hiding card");
        aircraft.Update(fadeOptions,snapshot,true); Pump(150); Check(aircraft.Visibility == Visibility.Visible && aircraft.Opacity > .99, "native fade-in restores the populated card");
        foreach (var font in new[] { 12,24,64 }) foreach (var width in new[] { 160d,640d,1920d })
        {
            var b = AircraftGeometry.Bounds(new() { Aircraft = options with { FontSize = font }, HostCameraSlot = 1 }, width, width / 1.777);
            Check(b.Width >= 0 && b.Height >= 0 && b.Left + b.Width <= width + .001 && b.Top + b.Height <= width / 1.777 + .001, "overlay geometry stays within camera bounds");
        }
    }
    private static IEnumerable<System.Windows.Controls.Image> VisualImages(DependencyObject parent)
    {
        if(parent is System.Windows.Controls.Image image)yield return image;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)foreach(var item in VisualImages(VisualTreeHelper.GetChild(parent,i)))yield return item;
    }
    private static string[] Texts(DependencyObject parent)
    {
        var output = new List<string>();
        if (parent is TextBlock text) output.Add(text.Text);
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) output.AddRange(Texts(VisualTreeHelper.GetChild(parent,i)));
        return output.ToArray();
    }
    private static IEnumerable<TextBlock> VisualTexts(DependencyObject parent)
    {
        if (parent is TextBlock text) yield return text;
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            foreach (var child in VisualTexts(VisualTreeHelper.GetChild(parent,i))) yield return child;
    }
    private static async Task ProviderSmoke(string[] args)
    {
        var options = new AircraftOptions { Latitude = double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), Longitude = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), RadiusMiles = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) };
        options.Validate();
        using var http = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 2_000_000 };
        var snapshot = await new AdsbLolProvider(http).FetchAsync(options, CancellationToken.None);
        Console.WriteLine($"Provider fetch and parsing succeeded. Freshness: {snapshot.Freshness(DateTimeOffset.UtcNow)}; airborne positions: {snapshot.Aircraft.Length}; matching radius/filters: {AircraftSelection.Nearby(options, snapshot, DateTimeOffset.UtcNow).Length}.");
    }
    private static void Pump(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_,_) => { timer.Stop(); frame.Continue = false; }; timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    private static AircraftTrack Track(DateTimeOffset now) => new() { Hex = "a12345", Callsign = "UAL123", Type = "B738", Registration = "N123EX", Latitude = 47.62, Longitude = -122.32, AltitudeFeet = 12400, SpeedKnots = 285, TrackDegrees = 245, VerticalRate = 640, PositionAt = now };
    private static async Task Backend()
    {
        await PhotoFallback();
        await NativePhotoDownload();
        Check(new AircraftTrack { Type = "SR22" }.ModelName == "Cirrus SR22", "short type expands to full model name");
        Check(RepresentativeAircraftPhotos.Identity(RepresentativeAircraftPhotos.Key(new AircraftTrack { Type = "SR22" })!).Model == "Cirrus SR22", "private SR22 receives representative photo lookup");
        Check(AircraftDetails.Parse("""{"response":{"aircraft":{"type":"PA-32R-301T","manufacturer":"Piper"}}}""").Model == "Piper PA-32R-301T", "manufacturer retained without duplication");
        Check(AircraftModels.Name("Unknown model") == "Unknown model", "unknown aircraft model is not guessed");
        var airportPhoto = AirportAircraftPhotos.Parse("""{"status":200,"data":[{"image":"https://airport-data.com/images/aircraft/thumbnails/1.jpg","link":"https://airport-data.com/aircraft/photo/1","photographer":"Test"}]}""");
        Check(airportPhoto is { IsValid: true, Representative: false, Source: "Airport-Data.com" }, "second source parses attributed exact aircraft thumbnail");
        Check((airportPhoto! with { Url = "https://example.com/photo.jpg" }).IsValid == false, "second source rejects unrelated image hosts");
        var representativeKey = RepresentativeAircraftPhotos.Key(new AircraftTrack { Type = "B738", Airline = "Alaska Airlines" })!;
        var representativeJson = """{"query":{"pages":{"1":{"title":"File:Alaska Airlines Boeing 737-800.jpg","imageinfo":[{"thumburl":"https://upload.wikimedia.org/example.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Example.jpg","extmetadata":{"Artist":{"value":"<b>Photographer</b>"},"LicenseShortName":{"value":"CC BY-SA 4.0"}}}]}}}}""";
        var representative = RepresentativeAircraftPhotos.Parse(representativeJson,representativeKey);
        Check(representative is { IsValid: true, Representative: true, Photographer: "Photographer" }, "representative photo matches model and airline with attribution");
        Check(RepresentativeAircraftPhotos.Parse(representativeJson.Replace("Alaska Airlines Boeing", "United Airlines Boeing"),representativeKey) is null, "representative photo rejects another airline");
        Check(RepresentativeAircraftPhotos.Parse(representativeJson.Replace("737-800.jpg", "747-400.jpg"),representativeKey) is null, "representative photo rejects another model");
        Check(RepresentativeAircraftPhotos.Parse(representativeJson.Replace("CC BY-SA 4.0", "All rights reserved"),representativeKey) is null, "representative photo requires reusable license");
        Check(RepresentativeAircraftPhotos.Parse(representativeJson.Replace("upload.wikimedia.org", "example.com"),representativeKey) is null, "representative photos restrict image hosts");
        var now = DateTimeOffset.UtcNow;
        var o = new AircraftOptions { Latitude = 47.6062, Longitude = -122.3321, ShowPhoto = false, Fields = ["type", "altitude", "speed", "distance", "track", "verticalRate"] };
        (o with { Latitude = 47.123456789012345, Longitude = -122.98765432109876 }).Validate();
        Check(true, "full-precision GPS coordinates accepted");
        var photo = AircraftPhotos.Parse("""{"photos":[{"thumbnail_large":{"src":"https://t.plnspttrs.net/test.jpg"},"link":"https://www.planespotters.net/photo/1","photographer":"Test Photographer"}]}""");
        Check(photo is { IsValid: true, Photographer: "Test Photographer" }, "photo metadata retains photographer and source");
        Check(AircraftPhotos.Parse("""{"photos":[]}""") is null, "missing photo falls back to aircraft icon");
        Check(photo is not null && !(photo with { Url = "https://example.org/test.jpg" }).IsValid && !(photo with { Link = "javascript:alert(1)" }).IsValid, "photo URLs restricted to provider hosts");
        var detail = AircraftDetails.Parse("""{"response":{"aircraft":{"registered_owner":"Example Owner LLC"},"flightroute":{"airline":{"name":"Example Airline"},"destination":{"iata_code":"SEA","municipality":"Seattle"}}}}""");
        Check(detail.Owner == "Example Owner LLC" && detail.Airline == "Example Airline" && detail.Destination == "SEA · Seattle", "owner and operator remain distinct, destination includes airport and city");
        Check(AircraftDetails.Parse("""{"response":"unknown callsign"}""") == new AircraftDetail(), "unknown route remains unavailable");
        var json = "{\"now\":" + now.ToUnixTimeMilliseconds() + ",\"ac\":[{\"hex\":\"a12345\",\"flight\":\"UAL123  \",\"lat\":47.62,\"lon\":-122.32,\"seen_pos\":2,\"alt_baro\":12400,\"gs\":285},{\"hex\":\"b12345\",\"lat\":47.62,\"lon\":-122.32,\"seen_pos\":2,\"alt_baro\":\"ground\"},{\"hex\":\"c12345\",\"lat\":47.62,\"lon\":-122.32,\"seen_pos\":120}]}";
        var parsed = AdsbLolProvider.Parse(json, o.CacheKey, now);
        var empty = AdsbLolProvider.Parse("{\"now\":" + now.ToUnixTimeMilliseconds() + ",\"ac\":[],\"msg\":\"No error\",\"total\":0}", o.CacheKey, now);
        Check(empty.Freshness(now) == "fresh" && !empty.RefreshFailed && empty.Aircraft.Length == 0, "successful empty provider response remains fresh, never unavailable");
        foreach (var invalid in new[] { "[]", "null", "{}", "{\"now\":0,\"ac\":[]}" })
        {
            try { AdsbLolProvider.Parse(invalid,o.CacheKey,now); throw new Exception("invalid provider response accepted"); } catch (InvalidDataException) { }
        }
        Check(true,"malformed and expired provider responses are recoverable");
        Check(parsed.Aircraft.Length == 1 && parsed.Aircraft[0].Callsign == "UAL123", "provider removes ground and old-position reports, trims callsigns");
        Check(parsed.Aircraft[0].PositionAt == now.AddSeconds(-2).AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond)), "position timestamp uses observation time minus seen_pos");
        Check(AircraftSelection.Nearby(o, parsed, now).Length == 1, "nearby airborne aircraft selected");
        Check(AircraftSelection.ShouldReplaceCamera(o, parsed, now), "fresh nearby aircraft activates camera replacement");
        Check(!AircraftSelection.ShouldReplaceCamera(o, parsed with { Aircraft = [] }, now) &&
            !AircraftSelection.ShouldReplaceCamera(o, parsed with { RefreshFailed = true }, now) &&
            !AircraftSelection.ShouldReplaceCamera(o, parsed, now.AddSeconds(31)) &&
            !AircraftSelection.ShouldReplaceCamera(o with { MinimumAltitudeFeet = 13000 }, parsed, now), "camera returns on empty, failed, stale or filtered traffic");
        Check(AircraftSelection.Nearby(o with { MinimumAltitudeFeet = 13000 }, parsed, now).Length == 0, "altitude filter excludes aircraft");
        Check(AircraftSelection.Nearby(o, parsed, now.AddSeconds(65)).Length == 0, "old individual positions expire even if feed exists");
        Check(AircraftSelection.Nearby(o with { Latitude = 0, Longitude = 0 }, parsed, now).Length == 0, "radius filters distant aircraft");
        Check(AircraftSelection.DistanceMiles(0,179.99,0,-179.99) < 2, "distance handles antimeridian");
        Check(AircraftSelection.Metric("altitude", Track(now), o with { Units = "metric" }) == "ALT 3,780 m", "metric altitude conversion");
        try { (o with { MinimumAltitudeFeet=10000,MaximumAltitudeFeet=1000 }).Validate(); throw new Exception("range accepted"); } catch (InvalidDataException) { }
        try { (o with { Longitude=double.NaN }).Validate(); throw new Exception("NaN accepted"); } catch (InvalidDataException) { }
        var directory=Path.Combine(Path.GetTempPath(),"RTSPView-aircraft-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"settings.json");await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new AppSettings { SchemaVersion=16 }));
            var store=new JsonSettingsStore(path);var settings=await store.LoadAsync();Check(settings.AircraftOverlays.Count==0,"stable configuration upgrades with aircraft off");
            var tile=new WallTile { Kind="aircraft", ItemId="aircraft-1", Aircraft=o, Row=2, Column=2 };
            settings=settings with { Layouts=[settings.Layouts[0] with { Tiles=settings.Layouts[0].Tiles.Take(8).Append(tile).ToArray() }], AircraftOverlays=[new() { HostCameraSlot=1,Aircraft=o }] };
            await store.SaveAsync(settings);var loaded=await store.LoadAsync();
            Check(loaded.Layouts[0].Tiles.Last().Aircraft!.CacheKey==o.CacheKey&&loaded.AircraftOverlays.Count==1,"aircraft tile and overlay round trip");
            Check(File.Exists(path+".before-aircraft.json"),"stable rollback backup retained");
            var betaPath = Path.Combine(directory, "beta-settings.json");
            await File.WriteAllTextAsync(betaPath, JsonSerializer.Serialize(settings with { SchemaVersion = 17 }));
            var betaStore = new JsonSettingsStore(betaPath); await betaStore.SaveAsync(await betaStore.LoadAsync());
            Check((await betaStore.LoadAsync()).SchemaVersion == AppSettings.CurrentSchemaVersion && File.Exists(betaPath + ".before-aircraft-details.json"), "beta.1 upgrade keeps schema-17 rollback backup");
            Check(JsonSettingsStore.ParseImport(JsonSerializer.Serialize(loaded)).AircraftOverlays.Count==1,"aircraft export/import round trip");
            Check(StreamCatalog.DeleteCamera(loaded,1).AircraftOverlays.Count==0,"host deletion removes aircraft overlay");
            Check(WeatherConfiguration.OnlyPresentationChanged(loaded,loaded with { AircraftOverlays=[] }),"aircraft appearance uses isolated viewer update");
            var conditional = loaded with { Layouts = [loaded.Layouts[0] with { Tiles = loaded.Layouts[0].Tiles.Select(t => t.CameraSlot == 1 ? t with { Aircraft = o } : t).ToArray() }] };
            var widget = new WallWidget { Id = "layout-aircraft", Kind = "aircraft", Aircraft = o, X = 25, Y = 70 };
            var widgetSettings = loaded with { Layouts = [loaded.Layouts[0] with { Widgets = [widget] }] };
            widgetSettings.Normalize(); await store.SaveAsync(widgetSettings);
            Check((await store.LoadAsync()).Layouts[0].Widgets.Single().X == 25, "layout-specific widget placement survives save and reload");
            Check(WeatherConfiguration.OnlyPresentationChanged(loaded, widgetSettings), "moving a layout widget leaves camera playback signature unchanged");
            var anchored = widgetSettings with { Layouts = [widgetSettings.Layouts[0] with { Widgets = [widget with { HostCameraSlot = 1 }] }] };
            Check(StreamCatalog.DeleteCamera(anchored, 1).Layouts[0].Widgets.Single().HostCameraSlot == 0, "deleting anchor retains widget as freeform");
            try { WallLayout.Validate([loaded.Layouts[0] with { Widgets = [widget with { X = 101 }] }], loaded.ActiveLayoutId); throw new Exception("invalid position accepted"); } catch (InvalidDataException) { }
            conditional.Normalize();
            Check(WeatherConfiguration.OnlyPresentationChanged(loaded, conditional), "conditional replacement preserves camera layout and playback signature");
            await store.SaveAsync(conditional with { StorageRevision = (await store.LoadAsync()).StorageRevision });
            Check((await store.LoadAsync()).Layouts[0].Tiles.First().Aircraft is not null, "conditional camera configuration persists");
            try { WallLayout.Validate([loaded.Layouts[0] with { Tiles=[tile,tile] }],loaded.ActiveLayoutId); throw new Exception("duplicate accepted"); } catch(InvalidDataException) { }
            await AircraftCache.WriteAsync(directory,[parsed],CancellationToken.None);Check((await AircraftCache.ReadAsync(directory)).Single().Aircraft.Length==1,"cache round trip");
            await AircraftCache.WriteAsync(directory, [parsed with { Aircraft = [parsed.Aircraft[0] with { Type = "Cessna 182T Skylane single-engine aircraft" }] }], CancellationToken.None);
            Check((await AircraftCache.ReadAsync(directory)).Single().Aircraft.Length == 1, "full model names do not drop aircraft from the native cache");
            await File.WriteAllTextAsync(AircraftCache.PathFor(directory),"broken");Check((await AircraftCache.ReadAsync(directory)).Length==0,"corrupt cache does not affect settings");
            var provider=new FakeProvider();using var service=new AircraftService(directory,provider);await service.StartAsync(CancellationToken.None);
            for(var i=0;i<40&&service.Snapshots.Length==0;i++)await Task.Delay(100);
            await service.StopAsync(CancellationToken.None);Check(provider.Calls==1,"tile and overlay share one provider request");
            var widgetsOnly = await store.LoadAsync();
            widgetsOnly = widgetsOnly with { AircraftOverlays = [], Layouts = [widgetsOnly.Layouts[0] with { Tiles = widgetsOnly.Layouts[0].Tiles.Where(t => t.Kind == "camera").Select(t => t with { Aircraft = null }).ToArray(), Widgets = [widget] }] };
            await store.SaveAsync(widgetsOnly);
            var beforeDisable = await store.LoadAsync();
            await store.SaveAsync(beforeDisable with { Plugins = new() { Aircraft = false, Weather = false } });
            var disabledProvider = new FakeProvider(); using var disabledService = new AircraftService(directory, disabledProvider);
            await disabledService.StartAsync(CancellationToken.None); await Task.Delay(1200);
            Check(disabledProvider.Calls == 0, "disabled aircraft plugin makes no provider requests");
            Check(System.Text.Json.JsonSerializer.Serialize((await store.LoadAsync()).Layouts) == System.Text.Json.JsonSerializer.Serialize(beforeDisable.Layouts), "plugin switches preserve saved aircraft layouts");
            await store.SaveAsync((await store.LoadAsync()) with { Plugins = beforeDisable.Plugins });
            for (var i=0;i<30&&disabledProvider.Calls==0;i++) await Task.Delay(100);
            Check(disabledProvider.Calls == 1, "re-enabled aircraft plugin resumes saved feed");
            await disabledService.StopAsync(CancellationToken.None);
            var delayedProvider = new DelayedProvider(); using var refreshing = new AircraftService(directory, delayedProvider);
            await refreshing.StartAsync(CancellationToken.None);
            await delayedProvider.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(18));
            Check(AircraftSelection.ShouldReplaceCamera(o, refreshing.Snapshots.Single(), DateTimeOffset.UtcNow), "in-progress refresh retains fresh matching aircraft");
            Check((await AircraftCache.ReadAsync(directory)).Single().Aircraft.Length == 1, "native cache retains aircraft while refresh is pending");
            delayedProvider.Complete.TrySetResult(true);
            for (var i=0; i<40 && refreshing.Snapshots.Single().Aircraft.Length>0; i++) await Task.Delay(50);
            Check(refreshing.Snapshots.Single().Aircraft.Length == 0, "completed empty refresh clears departed aircraft");
            await refreshing.StopAsync(CancellationToken.None);
            using var failing = new AircraftService(directory, new FailedProvider()); await failing.StartAsync(CancellationToken.None);
            for (var i = 0; i < 40 && failing.Snapshots.Length == 0; i++) await Task.Delay(100);
            await failing.StopAsync(CancellationToken.None);
            Check(failing.Snapshots.Single().LastError?.Contains("HTTP 503") == true && failing.Snapshots.Single().NextRetryAt > DateTimeOffset.UtcNow, "provider failures expose safe reason and retry time");
            Check((await AircraftCache.ReadAsync(directory)).Single().LastError?.Contains("HTTP 503") == true, "viewer receives the provider failure reason through its cache");
            Check((await store.LoadAsync()).Cameras.SequenceEqual(settings.Cameras),"aircraft updates leave cameras untouched");
        }
        finally { Directory.Delete(directory,true); }
    }
    private static async Task NativePhotoDownload()
    {
        var photo = new AircraftPhoto("https://t.plnspttrs.net/first.jpg", "https://www.planespotters.net/photo/1", "Test");
        using var handler = new ImageDownloadHandler(); using var http = new System.Net.Http.HttpClient(handler);
        var bytes = await AircraftPhotoImages.Download(http,photo,CancellationToken.None);
        Check(bytes.SequenceEqual(new byte[] {1,2,3}) && handler.Calls == 2 && handler.Identified, "native image identifies app and follows trusted CDN redirect");
        using var blockedHandler = new ImageDownloadHandler { Redirect = "https://example.com/image.jpg" }; using var blockedHttp = new System.Net.Http.HttpClient(blockedHandler);
        try { await AircraftPhotoImages.Download(blockedHttp,photo,CancellationToken.None); throw new Exception("untrusted redirect accepted"); } catch (System.Net.Http.HttpRequestException) { }
        Check(blockedHandler.Calls == 1, "native image rejects redirects outside its provider");
        using var deniedHandler = new ImageDownloadHandler { Denied = true }; using var deniedHttp = new System.Net.Http.HttpClient(deniedHandler);
        try { await AircraftPhotoImages.Download(deniedHttp,photo,CancellationToken.None); throw new Exception("denied image accepted"); } catch (System.Net.Http.HttpRequestException) { }
        Check(deniedHandler.Calls == 1, "native image denial remains a recoverable failure");
    }
    private sealed class ImageDownloadHandler : System.Net.Http.HttpMessageHandler
    {
        public int Calls; public bool Identified; public string Redirect = "/actual.jpg"; public bool Denied;
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Identified = request.Headers.UserAgent.ToString().Contains("RTSPView/") && request.Headers.Accept.ToString().Contains("image/jpeg");
            var response = new System.Net.Http.HttpResponseMessage(Denied ? System.Net.HttpStatusCode.Forbidden : Calls == 1 ? System.Net.HttpStatusCode.Redirect : System.Net.HttpStatusCode.OK);
            if (Calls == 1 && !Denied) response.Headers.Location = new Uri(Redirect,UriKind.RelativeOrAbsolute);
            response.Content = new System.Net.Http.ByteArrayContent([1,2,3]); return Task.FromResult(response);
        }
    }
    private static async Task PhotoFallback()
    {
        using var handler = new PhotoHandler(); using var http = new System.Net.Http.HttpClient(handler);
        var photos = new AircraftPhotos(http); using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        photos.Request("a12345"); var worker = photos.RunAsync(stop.Token);
        try
        {
            while (photos.Revision < 1) await Task.Delay(25, stop.Token);
            Check(photos.Find("a12345") is null, "empty hex photo result remains empty");
            photos.Request("a12345", " n2660q ");
            while (photos.Revision < 2) await Task.Delay(25, stop.Token);
            Check(photos.Find("a12345", "N2660Q") is { IsValid: true }, "late registration recovers photo after cached hex miss");
            Check(handler.Paths.SequenceEqual(new[] { "/pub/photos/hex/a12345", "/pub/photos/reg/N2660Q" }), "photo fallback uses normalized exact registration");
            photos.Request("a12345", "N2660Q"); await Task.Delay(2100, stop.Token);
            Check(handler.Paths.Count == 2, "photo lookup caches both hex miss and registration hit");
            Check(photos.Find("a12345", "N756MM") is null, "registration photo does not leak to another tail number");
            var other = new AircraftTrack { Hex = "b12345", Type = "C172", Airline = "Alaska Airlines" };
            photos.Request(other);
            while(photos.Revision < 3) await Task.Delay(25,stop.Token);
            photos.Request(other);
            while(photos.Revision < 4) await Task.Delay(25,stop.Token);
            photos.Request(other);
            while(photos.Revision < 5) await Task.Delay(25,stop.Token);
            Check(photos.Find(other) is { Representative: true, Source: "Wikimedia Commons" }, "empty exact lookup falls back to model and airline photo");
            Check(photos.Find(other with { Hex = "a12345", Registration = "N2660Q" }) is { Representative: false }, "exact registration photo takes priority over representative photo");
        }
        finally { stop.Cancel(); try { await worker; } catch (OperationCanceledException) { } }
    }
    private sealed class PhotoHandler : System.Net.Http.HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            var body = request.RequestUri.AbsolutePath.Contains("/reg/") ? """{"photos":[{"thumbnail_large":{"src":"https://t.plnspttrs.net/test.jpg"},"link":"https://www.planespotters.net/photo/1","photographer":"Test Photographer"}]}""" : """{"photos":[]}""";
            if(request.RequestUri.Host=="commons.wikimedia.org") body = """{"query":{"pages":{"1":{"title":"File:Alaska Airlines Cessna 172.jpg","imageinfo":[{"thumburl":"https://upload.wikimedia.org/example.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Example.jpg","extmetadata":{"Artist":{"value":"Photographer"},"LicenseShortName":{"value":"CC BY-SA 4.0"}}}]}}}}""";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(body) });
        }
    }
    private sealed class FakeProvider : IAircraftProvider
    {
        public int Calls;
        public Task<AircraftSnapshot> FetchAsync(AircraftOptions options,CancellationToken token) { Calls++;return Task.FromResult(new AircraftSnapshot {Key=options.CacheKey,FetchedAt=DateTimeOffset.UtcNow,Aircraft=[Track(DateTimeOffset.UtcNow) with { Hex = "sample", Callsign = "" }]}); }
    }
    private sealed class DelayedProvider : IAircraftProvider
    {
        private int _calls;
        public TaskCompletionSource<bool> RefreshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AircraftSnapshot> FetchAsync(AircraftOptions options, CancellationToken token)
        {
            if (++_calls == 1) return new() { Key = options.CacheKey, FetchedAt = DateTimeOffset.UtcNow, Aircraft = [Track(DateTimeOffset.UtcNow) with { Hex = "sample", Callsign = "" }] };
            RefreshStarted.TrySetResult(true); await Complete.Task.WaitAsync(token);
            return new() { Key = options.CacheKey, FetchedAt = DateTimeOffset.UtcNow, Aircraft = [] };
        }
    }
    private sealed class FailedProvider : IAircraftProvider
    {
        public Task<AircraftSnapshot> FetchAsync(AircraftOptions options, CancellationToken token) => Task.FromException<AircraftSnapshot>(new System.Net.Http.HttpRequestException("Internal transport detail", null, System.Net.HttpStatusCode.ServiceUnavailable));
    }
}
