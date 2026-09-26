using System.Collections.Concurrent;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

public sealed record AircraftDetail(string Owner = "", string Airline = "", string Destination = "", string Model = "");

// Callsign routes are database lookups, not live flight plans or arrival predictions.
public sealed class AircraftDetails(HttpClient http)
{
    private readonly ConcurrentDictionary<string, (AircraftDetail Value, DateTimeOffset Until)> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private long _revision;
    public long Revision => Interlocked.Read(ref _revision);
    private AircraftDetail Read(string key) => _cache.TryGetValue(key, out var entry) && entry.Until > DateTimeOffset.UtcNow ? entry.Value : new();
    public AircraftTrack Enrich(AircraftTrack track) => track with { RegisteredOwner = Read("aircraft/" + track.Hex).Owner,
        Type = Read("aircraft/" + track.Hex).Model is { Length: > 0 } model ? model : track.Type,
        Airline = Read("callsign/" + track.Callsign).Airline, Destination = Read("callsign/" + track.Callsign).Destination };
    public void Request(AircraftTrack track)
    {
        void Queue(string key) { if (_pending.Count < 32 && (!_cache.TryGetValue(key, out var e) || e.Until <= DateTimeOffset.UtcNow)) _pending.TryAdd(key, 0); }
        if (track.Hex.Length == 6 && track.Hex.All(Uri.IsHexDigit)) Queue("aircraft/" + track.Hex);
        if (track.Callsign.Length is >= 3 and <= 10 && track.Callsign.All(char.IsAsciiLetterOrDigit)) Queue("callsign/" + track.Callsign);
    }
    public async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var key = _pending.Keys.FirstOrDefault();
            if (key is not null)
            {
                var detail = new AircraftDetail(); var retry = TimeSpan.FromHours(key.StartsWith("aircraft/") ? 24 : 1);
                try
                {
                    using var response = await http.GetAsync("https://api.adsbdb.com/v0/" + key, token);
                    if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                    {
                        response.EnsureSuccessStatusCode();
                        detail = Parse(await response.Content.ReadAsStringAsync(token));
                    }
                }
                catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
                {
                    if (token.IsCancellationRequested) return;
                    retry = TimeSpan.FromMinutes(30); await Task.Delay(TimeSpan.FromSeconds(30), token);
                }
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
        return new(Text(Child(response, "aircraft"), "registered_owner"), Text(Child(route, "airline"), "name"),
            string.Join(" · ", new[] { code, Text(destination, "municipality") }.Where(s => s.Length > 0)), Text(Child(response, "aircraft"), "type"));
    }
}
