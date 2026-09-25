using System.Collections.Concurrent;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

// Independent, bounded metadata cache. Missing photos are cached too.
public sealed class AircraftPhotos(HttpClient http)
{
    private readonly ConcurrentDictionary<string, (AircraftPhoto? Photo, DateTimeOffset Until)> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private long _revision;
    public long Revision => Interlocked.Read(ref _revision);
    public AircraftPhoto? Find(string hex) => _cache.TryGetValue(hex, out var entry) && entry.Until > DateTimeOffset.UtcNow ? entry.Photo : null;
    public void Request(string hex)
    {
        if (hex.Length != 6 || hex.Any(c => !Uri.IsHexDigit(c)) || _pending.Count >= 32) return;
        if (!_cache.TryGetValue(hex, out var entry) || entry.Until <= DateTimeOffset.UtcNow) _pending.TryAdd(hex, 0);
    }
    public async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var hex = _pending.Keys.FirstOrDefault();
            if (hex is not null)
            {
                AircraftPhoto? photo = null;
                var retry = TimeSpan.FromHours(24);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.planespotters.net/pub/photos/hex/" + hex);
                    request.Headers.UserAgent.ParseAdd("RTSPView/1.0.47 (+https://github.com/GalacticaActual75/RTSPView/issues)");
                    using var response = await http.SendAsync(request, token);
                    response.EnsureSuccessStatusCode();
                    photo = Parse(await response.Content.ReadAsStringAsync(token));
                }
                catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
                {
                    if (token.IsCancellationRequested) return;
                    retry = TimeSpan.FromHours(1);
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                }
                if (_cache.Count >= 128) foreach (var key in _cache.OrderBy(p => p.Value.Until).Take(32).Select(p => p.Key)) _cache.TryRemove(key, out _);
                _cache[hex] = (photo, DateTimeOffset.UtcNow + retry);
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
