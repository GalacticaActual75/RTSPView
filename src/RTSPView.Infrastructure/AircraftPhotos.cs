using System.Collections.Concurrent;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

// Independent, bounded metadata cache. Missing photos are cached too.
public sealed class AircraftPhotos(HttpClient http)
{
    private readonly ConcurrentDictionary<string, (AircraftPhoto? Photo, DateTimeOffset Until, bool Empty)> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private long _revision;
    public long Revision => Interlocked.Read(ref _revision);
    private static string? RegistrationKey(string? registration)
    {
        var value = registration?.Trim().ToUpperInvariant();
        return value is { Length: >= 2 and <= 12 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? "reg/" + value : null;
    }
    private AircraftPhoto? Cached(string key) => _cache.TryGetValue(key, out var entry) && entry.Until > DateTimeOffset.UtcNow ? entry.Photo : null;
    public AircraftPhoto? Find(string hex, string? registration = null) => Cached("hex/" + hex.ToLowerInvariant()) ?? (RegistrationKey(registration) is { } key ? Cached(key) : null);
    public AircraftPhoto? Find(AircraftTrack track) => Find(track.Hex,track.Registration) ?? (RepresentativeAircraftPhotos.Key(track) is { } key ? Cached(key) : null);
    public void Request(AircraftTrack track)
    {
        Request(track.Hex,track.Registration);
        if(Find(track.Hex,track.Registration) is not null || !_cache.ContainsKey("hex/" + track.Hex.ToLowerInvariant())) return;
        if(RepresentativeAircraftPhotos.Key(track) is { } key && _pending.Count < 32 && (!_cache.TryGetValue(key,out var entry) || entry.Until <= DateTimeOffset.UtcNow)) _pending.TryAdd(key,0);
    }
    public void Request(string hex, string? registration = null)
    {
        if (hex.Length != 6 || hex.Any(c => !Uri.IsHexDigit(c)) || _pending.Count >= 32) return;
        var key = "hex/" + hex.ToLowerInvariant();
        if (!_cache.TryGetValue(key, out var entry) || entry.Until <= DateTimeOffset.UtcNow) _pending.TryAdd(key, 0);
        // Only fall back after a successful empty response; never bypass provider errors.
        else if (entry.Photo is null && entry.Empty && RegistrationKey(registration) is { } reg &&
            (!_cache.TryGetValue(reg, out var registered) || registered.Until <= DateTimeOffset.UtcNow)) _pending.TryAdd(reg, 0);
    }
    public async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var hex = _pending.Keys.FirstOrDefault();
            if (hex is not null)
            {
                AircraftPhoto? photo = null;
                var empty = false;
                var retry = TimeSpan.FromHours(24);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, hex.StartsWith("model/") ? RepresentativeAircraftPhotos.Url(hex) : "https://api.planespotters.net/pub/photos/" + hex);
                    request.Headers.UserAgent.ParseAdd("RTSPView/1.0.47 (+https://github.com/GalacticaActual75/RTSPView/issues)");
                    using var response = await http.SendAsync(request, token);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync(token);
                    photo = hex.StartsWith("model/") ? RepresentativeAircraftPhotos.Parse(json,hex) : Parse(json);
                    empty = photo is null;
                }
                catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
                {
                    if (token.IsCancellationRequested) return;
                    retry = TimeSpan.FromHours(1);
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                }
                if (_cache.Count >= 128) foreach (var key in _cache.OrderBy(p => p.Value.Until).Take(32).Select(p => p.Key)) _cache.TryRemove(key, out _);
                _cache[hex] = (photo, DateTimeOffset.UtcNow + retry, empty);
                _pending.TryRemove(hex, out _);
                Interlocked.Increment(ref _revision);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
    }
    public static AircraftPhoto? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("photos", out var photos) || photos.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in photos.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("thumbnail_large", out var thumbnail) || thumbnail.ValueKind != JsonValueKind.Object) continue;
            static string Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var photo = new AircraftPhoto(Text(thumbnail, "src"), Text(item, "link"), Text(item, "photographer"));
            if (photo.IsValid) return photo;
        }
        return null;
    }
}
