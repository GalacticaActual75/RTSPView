using System.Net;
using System.Net.Http;
using RTSPView.Core;
using RTSPView.Infrastructure;

internal static class AircraftPhotoFallbackChecks
{
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); Console.WriteLine("PASS " + message); }
    public static async Task Run()
    {
        var track = new AircraftTrack { Hex="a71518", Registration="N5555U", Type="CC11", ResolvedModel="CubCrafters Carbon Cub (CCK-1865)" };
        Check(RepresentativeAircraftPhotos.FamilyKey(track) == "model/Carbon%20Cub|", "registry kit suffix resolves representative family query");
        Check(RepresentativeAircraftPhotos.FamilyKey(track with { ResolvedModel="Unknown Aircraft" }) is null, "unknown aircraft family is not guessed");
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var events = new System.Collections.Concurrent.ConcurrentQueue<string>(); var photos = new AircraftPhotos(http, events.Enqueue);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(24)); var worker = photos.RunAsync(stop.Token);
        var second = track with { Hex="a11111", Registration="N11111" };
        try
        {
            while (photos.Find(track) is null || photos.Find(second) is null)
            {
                photos.Request(track); photos.Request(second); await Task.Delay(40, stop.Token);
            }
            Check(photos.Find(track) is { Representative:true, Source:"Wikimedia Commons" }, "empty exact providers resolve a labeled family photo");
            Check(handler.AirportCalls == 2 && handler.LastAirport - handler.FirstAirport < TimeSpan.FromSeconds(10), "normal airport 404 does not stall other aircraft behind error cooldown");
            Check(events.Any(e=>e.Contains("status=404")&&e.Contains("result=no-photo")) && !events.Any(e=>e.Contains("result=failed")), "photo logs distinguish normal misses from failures");
            Check(events.Any(e=>e.Contains("result=ready")&&e.Contains("image=")), "photo ready log can be correlated with native decoder log");
        }
        finally { stop.Cancel(); try { await worker; } catch (OperationCanceledException) { } }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int AirportCalls; public DateTimeOffset FirstAirport, LastAirport;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "airport-data.com")
            {
                if (++AirportCalls == 1) FirstAirport=DateTimeOffset.UtcNow; LastAirport=DateTimeOffset.UtcNow;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content=new StringContent("""{"status":404,"error":"Aircraft thumbnail not found."}""") });
            }
            var json = """{"photos":[]}""";
            if (uri.Host == "commons.wikimedia.org") json = Uri.UnescapeDataString(uri.Query).Contains("CCK-1865") ? """{"query":{"pages":{}}}""" : """
                {"query":{"pages":{"1":{"title":"File:Carbon Cub aircraft.jpg","imageinfo":[{"thumburl":"https://upload.wikimedia.org/example.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Carbon_Cub_aircraft.jpg","extmetadata":{"Artist":{"value":"Fixture Photographer"},"LicenseShortName":{"value":"CC BY-SA 3.0"}}}]}}}}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(json) });
        }
    }
}
