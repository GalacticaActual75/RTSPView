using System.Globalization;
using System.Net;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

public interface IAircraftProvider
{
    Task<AircraftSnapshot> FetchAsync(AircraftOptions options, CancellationToken token);
}

public sealed class AdsbLolProvider(HttpClient http) : IAircraftProvider
{
    public async Task<AircraftSnapshot> FetchAsync(AircraftOptions options, CancellationToken token)
    {
        var radiusNm = Math.Ceiling(options.RadiusMiles / 1.150779448);
        var url = FormattableString.Invariant($"https://api.adsb.lol/v2/point/{options.Latitude:F4}/{options.Longitude:F4}/{radiusNm:0}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("RTSPView/1.0 AircraftWidget");
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromMinutes(1);
            throw new AircraftRateLimitException(retry);
        }
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(token), options.CacheKey, DateTimeOffset.UtcNow);
    }

    public static AircraftSnapshot Parse(string json, string key, DateTimeOffset fetched)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ac", out var aircraft) || aircraft.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Aircraft response is missing its aircraft list.");
        var milliseconds = Number(root, "now");
        if (milliseconds is null || milliseconds < 0 || milliseconds > 253402300799000)
            throw new InvalidDataException("Aircraft response is missing its observation time.");
        var observed = DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds.Value);
        if (observed > fetched.AddSeconds(30) || fetched - observed > TimeSpan.FromSeconds(90))
            throw new InvalidDataException($"Aircraft timestamp is {Math.Abs((observed - fetched).TotalSeconds):0} seconds {(observed > fetched ? "ahead of" : "behind")} the host clock. Check Windows time synchronization; the provider may also be serving old data.");
        var tracks = new List<AircraftTrack>();
        foreach (var a in aircraft.EnumerateArray().Take(2000))
        {
            if (a.ValueKind != JsonValueKind.Object) continue;
            var lat = Number(a, "lat"); var lon = Number(a, "lon"); var seen = Number(a, "seen_pos");
            var hex = Text(a, "hex", 7);
            if (lat is null or < -90 or > 90 || lon is null or < -180 or > 180 || seen is null or < 0 or > 60 || hex.Length == 0 || Text(a, "alt_baro", 10) == "ground") continue;
            tracks.Add(new() { Hex = hex, Callsign = Text(a, "flight", 16), Registration = Text(a, "r", 20), Type = Text(a, "t", 20),
                Latitude = lat.Value, Longitude = lon.Value, PositionAt = observed.AddSeconds(-seen.Value),
                AltitudeFeet = Range(Number(a, "alt_baro") ?? Number(a, "alt_geom"), -2000, 100000),
                SpeedKnots = Range(Number(a, "gs"), 0, 3000), TrackDegrees = Range(Number(a, "track"), 0, 359.9999),
                VerticalRate = Range(Number(a, "baro_rate") ?? Number(a, "geom_rate"), -30000, 30000) });
        }
        return new() { Key = key, FetchedAt = fetched, Aircraft = tracks.DistinctBy(a => a.Hex).ToArray() };
    }
    private static double? Range(double? n, double min, double max) => n >= min && n <= max ? n : null;
    private static double? Number(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
    private static string Text(JsonElement e, string key, int max) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? new string((v.GetString() ?? "").Trim().Where(c => !char.IsControl(c)).Take(max).ToArray()) : "";
}

public sealed class AircraftRateLimitException(TimeSpan retryAfter) : HttpRequestException("Aircraft provider rate limit.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

public static class AircraftCache
{
    public static string PathFor(string directory) => Path.Combine(directory, "aircraft-cache.json");
    public static async Task<AircraftSnapshot[]> ReadAsync(string directory, CancellationToken token = default)
    {
        try
        {
            var path = PathFor(directory);
            if (!File.Exists(path) || new FileInfo(path).Length > 4_000_000) return [];
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
            return (await JsonSerializer.DeserializeAsync<AircraftSnapshot[]>(stream, cancellationToken: token) ?? [])
                .Where(s => s is not null && s.Key is { Length: <= 80 } && s.Aircraft is not null).Take(4)
                .Select(s => s with { Aircraft = s.Aircraft.Where(a => a is not null && a.Hex is { Length: > 0 and <= 7 } &&
                    a.Callsign is { Length: <= 16 } && a.Registration is { Length: <= 20 } && a.Type is { Length: <= 160 } &&
                    double.IsFinite(a.Latitude) && double.IsFinite(a.Longitude) && a.Latitude is >= -90 and <= 90 && a.Longitude is >= -180 and <= 180 &&
                    a.AltitudeFeet is null or (>= -2000 and <= 100000) && a.SpeedKnots is null or (>= 0 and <= 3000) &&
                    a.TrackDegrees is null or (>= 0 and < 360) && a.VerticalRate is null or (>= -30000 and <= 30000)).Take(2000).ToArray() }).ToArray();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }
    public static async Task WriteAsync(string directory, IEnumerable<AircraftSnapshot> snapshots, CancellationToken token)
    {
        var path = PathFor(directory);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(snapshots.Take(4)), token);
        File.Move(path + ".tmp", path, true);
    }
}
