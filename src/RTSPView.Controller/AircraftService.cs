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
    public AircraftSnapshot[] Snapshots => _cache.Values.ToArray();
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var source = provider ?? new AdsbLolProvider(_http);
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
        while (!token.IsCancellationRequested)
        {
            try
            {
                var settings = await store.LoadAsync(token);
                var areas = settings.Layouts.SelectMany(l => l.Tiles).Where(t => t.Kind == "aircraft").Select(t => t.Aircraft!)
                    .Concat(settings.AircraftOverlays.Where(o => o.Enabled).Select(o => o.Aircraft)).DistinctBy(o => o.CacheKey).Take(4).ToArray();
                var keys = areas.Select(o => o.CacheKey).ToHashSet();
                var changed = false;
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
                        _cache[area.CacheKey] = (_cache.GetValueOrDefault(area.CacheKey) ?? new() { Key = area.CacheKey }) with { RefreshFailed = true };
                        var delay = e is AircraftRateLimitException rate ? rate.RetryAfter : TimeSpan.FromSeconds(30);
                        _providerRetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(delay.TotalSeconds, 30, 86400));
                    }
                }
                if (changed) await AircraftCache.WriteAsync(directory, _cache.Values, token);
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
        app.MapGet("/api/aircraft", (AircraftService aircraft) => Results.Ok(aircraft.Snapshots)).RequireAuthorization();
        app.MapPut("/api/aircraft/overlays/{slot:int}", async (int slot, AircraftOverlay overlay) =>
        {
            await gate.WaitAsync();
            try
            {
                overlay = overlay with { HostCameraSlot = slot }; overlay.Validate();
                await using var transaction = await store.BeginWriteAsync();
                var settings = await transaction.LoadAsync();
                if (settings.DeletedCameraSlots.Contains(slot)) return Results.BadRequest(new { error = "Choose an existing camera." });
                await transaction.SaveAsync(settings with { AircraftOverlays = settings.AircraftOverlays.Where(o => o.HostCameraSlot != slot).Append(overlay).ToArray() });
                return Results.Ok(overlay);
            }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
            finally { gate.Release(); }
        }).RequireAuthorization();
    }
}
