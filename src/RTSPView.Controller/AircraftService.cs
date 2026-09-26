using System.Collections.Concurrent;
using System.Text.Json;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

// Shared feed runs independently of camera playback and the viewer command pipe.
public sealed class AircraftService(string directory, IAircraftProvider? provider = null) : BackgroundService
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 2_000_000 };
    private readonly ConcurrentDictionary<string, AircraftSnapshot> _cache = new();
    private readonly Dictionary<string, DateTimeOffset> _next = [];
    private DateTimeOffset _providerRetryAt;
    private AircraftPhotos? _photos;
    private AircraftDetails? _details;
    public AircraftSnapshot[] Snapshots => _cache.Values.Select(s => s with { Aircraft = s.Aircraft.Select(a => (_details?.Enrich(a) ?? a) with { Photo = _photos?.Find(a.Hex) }).ToArray() }).ToArray();
    protected override Task ExecuteAsync(CancellationToken token) =>
        PluginRunner.RunAsync(directory, f => f.Aircraft, RunEnabledAsync, token);
    private async Task RunEnabledAsync(CancellationToken token)
    {
        _next.Clear();
        _photos = new AircraftPhotos(_http);
        _details = new AircraftDetails(_http);
        await Task.WhenAll(FeedAsync(token), _photos.RunAsync(token), _details.RunAsync(token));
    }
    private async Task FeedAsync(CancellationToken token)
    {
        long photoRevision = -1, detailRevision = -1;
        var source = provider ?? new AdsbLolProvider(_http);
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
        while (!token.IsCancellationRequested)
        {
            try
            {
                var settings = await store.LoadAsync(token);
                var areas = settings.Layouts.SelectMany(l => l.Tiles).Where(t => t.Aircraft is not null).Select(t => t.Aircraft!)
                    .Concat(settings.AircraftOverlays.Where(o => o.Enabled).Select(o => o.Aircraft)).DistinctBy(o => o.CacheKey).Take(4).ToArray();
                var keys = areas.Select(o => o.CacheKey).ToHashSet();
                var changed = photoRevision != _photos!.Revision || detailRevision != _details!.Revision;
                photoRevision = _photos.Revision; detailRevision = _details!.Revision;
                foreach (var key in _cache.Keys.Where(k => !keys.Contains(k))) { _cache.TryRemove(key, out _); _next.Remove(key); changed = true; }
                foreach (var area in areas)
                {
                    var now = DateTimeOffset.UtcNow;
                    if (now < _providerRetryAt || (_next.TryGetValue(area.CacheKey, out var next) && next > now)) continue;
                    changed = true;
                    try
                    {
                        _cache[area.CacheKey] = await source.FetchAsync(area, token);
                        _next[area.CacheKey] = DateTimeOffset.UtcNow.AddSeconds(10);
                    }
                    catch (Exception e) when (e is HttpRequestException or JsonException or InvalidDataException or OperationCanceledException)
                    {
                        if (token.IsCancellationRequested) return;
                        var delay = e is AircraftRateLimitException rate ? rate.RetryAfter : TimeSpan.FromSeconds(30);
                        _providerRetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(delay.TotalSeconds, 30, 86400));
                        var reason = e switch
                        {
                            AircraftRateLimitException => "Aircraft provider rate limit reached.",
                            OperationCanceledException => "Aircraft provider request timed out.",
                            HttpRequestException http when http.StatusCode.HasValue => $"Aircraft provider returned HTTP {(int)http.StatusCode.Value}.",
                            HttpRequestException http => http.HttpRequestError switch
                            {
                                HttpRequestError.NameResolutionError => "The host could not resolve the aircraft provider's address (DNS).",
                                HttpRequestError.SecureConnectionError => "The host could not establish a secure connection to the aircraft provider (TLS).",
                                HttpRequestError.ProxyTunnelError => "The host's proxy could not connect to the aircraft provider.",
                                _ => "Could not connect to the aircraft provider."
                            },
                            InvalidDataException invalid => invalid.Message,
                            _ => "Aircraft provider returned invalid or outdated data."
                        };
                        _cache[area.CacheKey] = (_cache.GetValueOrDefault(area.CacheKey) ?? new() { Key = area.CacheKey }) with
                            { RefreshFailed = true, LastError = reason, NextRetryAt = _providerRetryAt };
                    }
                }
                foreach (var options in settings.Layouts.SelectMany(l => l.Tiles).Where(t => t.Aircraft is not null).Select(t => t.Aircraft!).Concat(settings.AircraftOverlays.Where(o => o.Enabled).Select(o => o.Aircraft)))
                    foreach (var track in AircraftSelection.Nearby(options, _cache.GetValueOrDefault(options.CacheKey), DateTimeOffset.UtcNow).Take(32)) { if (options.ShowPhoto) _photos.Request(track.Hex); _details.Request(track); }
                if (changed) await AircraftCache.WriteAsync(directory, Snapshots, token);
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { /* Retry separately from cameras. */ }
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
    }
    public override void Dispose() { base.Dispose(); _http.Dispose(); }
}

public static class AircraftEndpoints
{
    public static void MapAircraft(this WebApplication app, JsonSettingsStore store, SemaphoreSlim gate)
    {
        app.MapGet("/api/aircraft", async (AircraftService aircraft) => Results.Ok((await store.LoadAsync()).Plugins.Aircraft ? aircraft.Snapshots : [])).RequireAuthorization();
        app.MapGet("/api/aircraft/search", async (string q, WeatherService weather, CancellationToken token) =>
        {
            if (!(await store.LoadAsync(token)).Plugins.Aircraft) return Results.NotFound();
            try { return Results.Ok(await weather.SearchAsync(q, token)); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException) { return Results.Json(new { error = "Location search is unavailable. Try again or enter coordinates." }, statusCode: 503); }
        }).RequireAuthorization();
        app.MapPut("/api/aircraft/overlays/{slot:int}", async (int slot, AircraftOverlay overlay) =>
        {
            await gate.WaitAsync();
            try
            {
                overlay = overlay with { HostCameraSlot = slot }; overlay.Validate();
                await using var transaction = await store.BeginWriteAsync();
                var settings = await transaction.LoadAsync();
                if (!settings.Plugins.Aircraft) return Results.NotFound();
                if (settings.DeletedCameraSlots.Contains(slot)) return Results.BadRequest(new { error = "Choose an existing camera." });
                await transaction.SaveAsync(settings with { AircraftOverlays = settings.AircraftOverlays.Where(o => o.HostCameraSlot != slot).Append(overlay).ToArray() });
                return Results.Ok(overlay);
            }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
            finally { gate.Release(); }
        }).RequireAuthorization();
    }
}
