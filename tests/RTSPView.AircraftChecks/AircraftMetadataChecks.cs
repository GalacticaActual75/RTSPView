using System.Net;
using System.Net.Http;
using System.Text.Json;
using RTSPView.Core;
using RTSPView.Infrastructure;

internal static class AircraftMetadataChecks
{
    // Only aircraft fields used by the fallback; no owner names or addresses are retained.
    private const string Registry = """
        <td data-label="Status">Valid</td>
        <td data-label="Mode S Code (Base 16 / Hex)">A71518</td>
        <td data-label="Type Registration">Individual</td>
        <td data-label="Manufacturer Name">AMATEUR BUILDER</td>
        <td data-label="Model">CCK-1865</td>
        <td data-label="Kit Manufacturer">CUB CRAFTERS INC</td>
        <td data-label="Kit Model">CCK-1865</td>
        """;
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); Console.WriteLine("PASS " + message); }
    public static async Task Run()
    {
        var parsed = FaaAircraftDetails.Parse(Registry, "a71518");
        Check(parsed.Owner == "Private Owner" && parsed.Model == "CubCrafters Carbon Cub (CCK-1865)", "verified individual registry record resolves owner and kit model");
        Check(FaaAircraftDetails.Parse(Registry, "a12345") == new AircraftDetail(), "registry hex mismatch rejected");
        Check(FaaAircraftDetails.Parse(Registry.Replace("Valid", "Expired"), "a71518") == new AircraftDetail(), "expired registry record rejected");
        Check(FaaAircraftDetails.Parse(Registry.Replace("Individual", "Corporation"), "a71518").Owner == "", "corporation is not guessed to be a private owner");
        Check(FaaAircraftDetails.Parse("<html>Unavailable</html>", "a71518") == new AircraftDetail(), "missing registry data stays unknown");
        Check(FaaAircraftDetails.CanLookup("N5555U") && !FaaAircraftDetails.CanLookup("N5555U&x=1") && !FaaAircraftDetails.CanLookup("G-ABCD"), "FAA lookup accepts only valid US registration shape");
        Check(AircraftModels.Name("CC11").Contains("CubCrafters") && !AircraftModels.Name("CC11").Contains("EX-2"), "broad aircraft type resolves family without guessing variant");
        Check(new AircraftOverlay().Aircraft.Fields.Contains("owner"), "new overlays show owner by default");
        foreach (var primary in new[] { HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK })
        {
            using var handler = new Handler(primary); using var http = new HttpClient(handler);
            var details = new AircraftDetails(http);
            var track = new AircraftTrack { Hex = "a71518", Registration = "n5555u", Callsign = "N5555U", Type = "CC11" };
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            details.Request(track); var worker = details.RunAsync(stop.Token);
            try
            {
                while (details.Revision < 1) await Task.Delay(20, stop.Token);
                var enriched = details.Enrich(track);
                Check(enriched.Type == "CC11" && enriched.ModelName.StartsWith("CubCrafters"), "enrichment preserves raw type and supplies display name: " + primary);
                Check(enriched.RegisteredOwner == (primary == HttpStatusCode.OK ? "Known Owner LLC" : "Private Owner"), "registry fallback preserves known owner: " + primary);
                Check(handler.Calls == (primary == HttpStatusCode.OK ? 1 : 2), "metadata uses registry only when needed: " + primary);
                var restored = JsonSerializer.Deserialize<AircraftTrack>(JsonSerializer.Serialize(enriched))!;
                Check(restored.ModelName == enriched.ModelName && restored.RegisteredOwner == enriched.RegisteredOwner, "wall cache and API retain resolved metadata");
                Check(RepresentativeAircraftPhotos.Key(enriched)?.Contains("CCK-1865") == true, "photo fallback uses resolved model");
                details.Request(track); await Task.Delay(2100, stop.Token);
                Check(handler.Calls == (primary == HttpStatusCode.OK ? 1 : 2), "metadata cache avoids duplicate requests and registration callsign lookup");
            }
            finally { stop.Cancel(); try { await worker; } catch (OperationCanceledException) { } }
        }
    }
    private sealed class Handler(HttpStatusCode primary) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (request.RequestUri!.Host == "registry.faa.gov")
            {
                Check(request.RequestUri.Query == "?nNumberTxt=5555U", "registry lookup uses normalized registration");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Registry) });
            }
            return Task.FromResult(new HttpResponseMessage(primary) { Content = new StringContent("""{"response":{"aircraft":{"registered_owner":"Known Owner LLC","type":"CCK-1865"}}}""") });
        }
    }
}
