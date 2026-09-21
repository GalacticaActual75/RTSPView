using System.Collections.Concurrent;
using System.Text.Json;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

// Weather never shares the playback command pipe or waits on the Viewer.
public sealed class WeatherService(string directory, IWeatherProvider? provider = null) : BackgroundService
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 2_000_000 };
    private readonly ConcurrentDictionary<string, WeatherSnapshot> _cache = new();
    private readonly Dictionary<string, DateTimeOffset> _next = [];
    private readonly SemaphoreSlim _searchGate = new(1, 1);
    private readonly Dictionary<string, (DateTimeOffset At, JsonElement Data)> _searches = [];
    private DateTimeOffset _lastSearch;
    public WeatherSnapshot[] Snapshots => _cache.Values.ToArray();

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        foreach (var item in await WeatherCache.ReadAsync(directory, token)) _cache[item.Key] = item;
        var source = provider ?? new OpenMeteoProvider(_http);
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
        while (!token.IsCancellationRequested)
        {
            try
            {
                var settings = await store.LoadAsync(token);
                var locations = settings.Layouts.SelectMany(l => l.Tiles).Where(t => t.Kind == "weather").Select(t => t.Weather!)
                    .Concat(settings.WeatherOverlays.Where(o => o.Enabled).Select(o => o.Weather)).DistinctBy(w => w.CacheKey).Take(32).ToArray();
                var keys = locations.Select(w => w.CacheKey).ToHashSet();
                var changed = false;
                foreach (var key in _cache.Keys.Where(k => !keys.Contains(k))) { _cache.TryRemove(key, out _); _next.Remove(key); changed = true; }
                foreach (var location in locations)
                {
                    var key = location.CacheKey; var now = DateTimeOffset.UtcNow;
                    if (_next.TryGetValue(key, out var next) && next > now) continue;
                    if (!_next.ContainsKey(key) && _cache.TryGetValue(key, out var recent) && recent.FetchedAt > now.AddMinutes(-15)) { _next[key] = recent.FetchedAt.AddMinutes(15); continue; }
                    try
                    {
                        _cache[key] = await source.FetchAsync(location, token);
                        _next[key] = now.AddMinutes(15).AddSeconds(Random.Shared.Next(30));
                    }
                    catch (Exception error) when (error is HttpRequestException or JsonException or InvalidDataException or OperationCanceledException or InvalidOperationException or KeyNotFoundException or ArgumentOutOfRangeException)
                    {
                        if (token.IsCancellationRequested) return;
                        _cache[key] = (_cache.GetValueOrDefault(key) ?? new WeatherSnapshot { Key = key }) with { RefreshFailed = true };
                        var delay = error is WeatherRateLimitException rate ? rate.RetryAfter : TimeSpan.FromMinutes(5);
                        _next[key] = now.Add(delay < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay).AddSeconds(Random.Shared.Next(30));
                    }
                    changed = true;
                }
                if (changed) await WeatherCache.WriteAsync(directory, _cache.Values, token);
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { /* Retry independently of camera supervision. */ }
            await Task.Delay(TimeSpan.FromSeconds(10), token);
        }
    }

    public async Task<JsonElement> SearchAsync(string query, CancellationToken token)
    {
        if (query.Trim().Length is < 2 or > 100) throw new InvalidDataException("Enter 2–100 characters to search for a location.");
        await _searchGate.WaitAsync(token);
        try
        {
            var key = query.Trim().ToLowerInvariant(); var now = DateTimeOffset.UtcNow;
            if (_searches.TryGetValue(key, out var cached) && now - cached.At < TimeSpan.FromHours(24)) return cached.Data;
            if (now - _lastSearch < TimeSpan.FromSeconds(1)) throw new InvalidDataException("Please wait a moment before searching again.");
            _lastSearch = now;
            using var response = await _http.GetAsync("https://geocoding-api.open-meteo.com/v1/search?count=8&language=en&name=" + Uri.EscapeDataString(query.Trim()), token);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var result = doc.RootElement.Clone();
            if (_searches.Count >= 100) _searches.Remove(_searches.MinBy(p => p.Value.At).Key);
            _searches[key] = (now, result); return result;
        }
        finally { _searchGate.Release(); }
    }
    public override void Dispose() { base.Dispose(); _http.Dispose(); }
}

public static class WeatherEndpoints
{
    public static void MapWeather(this WebApplication app, JsonSettingsStore store, SemaphoreSlim gate)
    {
        app.MapGet("/api/weather", (WeatherService weather) => Results.Ok(weather.Snapshots)).RequireAuthorization();
        app.MapGet("/api/weather/search", async (string q, WeatherService weather, CancellationToken token) =>
        {
            try { return Results.Ok(await weather.SearchAsync(q, token)); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException) { return Results.Json(new { error = "Location search is unavailable. Try again or enter coordinates." }, statusCode: 503); }
        }).RequireAuthorization();
        app.MapPut("/api/weather/overlays/{slot:int}", async (int slot, WeatherOverlay overlay) =>
        {
            await gate.WaitAsync();
            try
            {
                overlay = overlay with { HostCameraSlot = slot }; overlay.Validate();
                var settings = await store.LoadAsync();
                await store.SaveAsync(settings with { WeatherOverlays = settings.WeatherOverlays.Where(o => o.HostCameraSlot != slot).Append(overlay).ToArray() });
                return Results.Ok(overlay);
            }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
            finally { gate.Release(); }
        }).RequireAuthorization();
    }
}
