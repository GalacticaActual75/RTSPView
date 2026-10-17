using System.Collections.Concurrent;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

public sealed record AircraftDetail(string Owner = "", string Airline = "", string Destination = "", string Model = "", string Source = "");

// Callsign routes are database lookups, not live flight plans or arrival predictions.
public sealed class AircraftDetails(HttpClient http)
{
    private readonly ConcurrentDictionary<string, (AircraftDetail Value, DateTimeOffset Until)> _cache = new();
    private readonly ConcurrentDictionary<string, AircraftTrack> _pending = new();
    private readonly ConcurrentQueue<string> _queue = new();
    private long _revision;
    public long Revision => Interlocked.Read(ref _revision);
    private AircraftDetail Read(string key) => _cache.TryGetValue(key, out var entry) && entry.Until > DateTimeOffset.UtcNow ? entry.Value : new();
    public AircraftTrack Enrich(AircraftTrack track)
    {
        var aircraft = Read("aircraft/" + track.Hex.ToUpperInvariant() + "/" + track.Registration.Trim().ToUpperInvariant());
        var route = Read("callsign/" + track.Callsign.Trim().ToUpperInvariant());
        return track with { RegisteredOwner = Value(aircraft.Owner, track.RegisteredOwner),
            ResolvedModel = Value(aircraft.Model, track.ResolvedModel), DetailsSource = Value(aircraft.Source, track.DetailsSource),
            Airline = Value(route.Airline, track.Airline), Destination = Value(route.Destination, track.Destination) };
    }
    private static string Value(string value, string fallback) => AircraftSelection.HasDetailValue(value) ? value : fallback;
    public void Request(AircraftTrack track)
    {
        track = track with { Hex = track.Hex.ToUpperInvariant(), Registration = track.Registration.Trim().ToUpperInvariant(), Callsign = track.Callsign.Trim().ToUpperInvariant() };
        void Queue(string key) { if (_pending.Count < 32 && (!_cache.TryGetValue(key, out var e) || e.Until <= DateTimeOffset.UtcNow) && _pending.TryAdd(key, track)) _queue.Enqueue(key); }
        if (track.Hex.Length == 6 && track.Hex.All(Uri.IsHexDigit)) Queue("aircraft/" + track.Hex + "/" + track.Registration);
        if (track.Callsign.Length is >= 3 and <= 10 && track.Callsign.All(char.IsAsciiLetterOrDigit) && track.Callsign != track.Registration) Queue("callsign/" + track.Callsign);
    }
    public async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (_queue.TryDequeue(out var key) && _pending.TryGetValue(key, out var track))
            {
                var detail = new AircraftDetail(); var retry = TimeSpan.FromHours(key.StartsWith("aircraft/") ? 24 : 1);
                try
                {
                    var path = key.StartsWith("aircraft/") ? "aircraft/" + track.Hex : key;
                    using var response = await http.GetAsync("https://api.adsbdb.com/v0/" + path, token);
                    if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                    {
                        response.EnsureSuccessStatusCode();
                        detail = Parse(await response.Content.ReadAsStringAsync(token));
                    }
                }
                catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
                {
                    if (token.IsCancellationRequested) return;
                    retry = TimeSpan.FromMinutes(5);
                }
                if (key.StartsWith("aircraft/") && FaaAircraftDetails.CanLookup(track.Registration)
                    && (!AircraftSelection.HasDetailValue(detail.Owner) || !AircraftSelection.HasDetailValue(detail.Model)))
                {
                    try
                    {
                        using var response = await http.GetAsync("https://registry.faa.gov/AircraftInquiry/Search/NNumberResult?nNumberTxt=" + track.Registration[1..], token);
                        response.EnsureSuccessStatusCode();
                        var registry = FaaAircraftDetails.Parse(await response.Content.ReadAsStringAsync(token), track.Hex);
                        var used = (!AircraftSelection.HasDetailValue(detail.Owner) && registry.Owner.Length > 0)
                            || (!AircraftSelection.HasDetailValue(detail.Model) && registry.Model.Length > 0);
                        detail = detail with { Owner = Value(detail.Owner, registry.Owner), Model = Value(detail.Model, registry.Model), Source = used ? "FAA" : detail.Source };
                    }
                    catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Text.RegularExpressions.RegexMatchTimeoutException)
                    {
                        if (token.IsCancellationRequested) return;
                        retry = TimeSpan.FromMinutes(5);
                    }
                }
                if (detail == new AircraftDetail() && retry > TimeSpan.FromHours(1)) retry = TimeSpan.FromHours(1);
                if (_cache.Count >= 256) foreach (var old in _cache.OrderBy(p => p.Value.Until).Take(64).Select(p => p.Key)) _cache.TryRemove(old, out _);
                _cache[key] = (detail, DateTimeOffset.UtcNow + retry); _pending.TryRemove(key, out _); Interlocked.Increment(ref _revision);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
    }
    public static AircraftDetail Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        static JsonElement Child(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) ? v : default;
        static string Text(JsonElement e, string key) { var v = Child(e, key); return v.ValueKind == JsonValueKind.String ? new string((v.GetString() ?? "").Where(c => !char.IsControl(c)).Take(160).ToArray()) : ""; }
        var response = Child(doc.RootElement, "response"); var route = Child(response, "flightroute"); var destination = Child(route, "destination");
        var code = Text(destination, "iata_code"); if (code.Length == 0) code = Text(destination, "icao_code");
        var aircraft = Child(response, "aircraft");
        var rawModel = Text(aircraft, "type");
        var model = AircraftModels.Name(rawModel);
        var manufacturer = Text(aircraft, "manufacturer");
        if (model == rawModel && model.Length > 0 && manufacturer.Length > 0 && !model.Contains(manufacturer, StringComparison.OrdinalIgnoreCase)) model = manufacturer + " " + model;
        return new(Text(aircraft, "registered_owner"), Text(Child(route, "airline"), "name"),
            string.Join(" · ", new[] { code, Text(destination, "municipality") }.Where(s => s.Length > 0)), model);
    }
}
