using System.Globalization;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

public interface IWeatherProvider
{
    Task<WeatherSnapshot> FetchAsync(WeatherOptions location, CancellationToken token);
}

public sealed class OpenMeteoProvider(HttpClient client) : IWeatherProvider
{
    public async Task<WeatherSnapshot> FetchAsync(WeatherOptions location, CancellationToken token)
    {
        location.Validate();
        var coordinates = location.CacheKey.Split(',');
        var uri = "https://api.open-meteo.com/v1/forecast?latitude=" + coordinates[0] + "&longitude=" + coordinates[1] +
            "&current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,wind_direction_10m,weather_code,is_day" +
            "&hourly=temperature_2m,weather_code,precipitation_probability&daily=temperature_2m_max,temperature_2m_min,weather_code,sunrise,sunset&timezone=auto&timeformat=unixtime&forecast_days=7&wind_speed_unit=ms";
        using var response = await client.GetAsync(uri, token);
        if ((int)response.StatusCode == 429)
        {
            var wait = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromMinutes(15);
            throw new WeatherRateLimitException(wait);
        }
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(token), location.CacheKey, DateTimeOffset.UtcNow);
    }

    public static WeatherSnapshot Parse(string json, string key, DateTimeOffset fetched)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var current = root.GetProperty("current");
        static double? Number(JsonElement obj, string name) => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
        static double? At(JsonElement obj, string name, int i) => obj.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array && i < values.GetArrayLength() && values[i].ValueKind == JsonValueKind.Number && values[i].TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
        static DateTimeOffset? Instant(double? value) => value is >= -62135596800 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds((long)value.Value) : null;
        var zone = root.GetProperty("timezone").GetString() ?? "UTC";
        var hours = new List<WeatherHour>();
        if (root.TryGetProperty("hourly", out var hourly) && hourly.TryGetProperty("time", out var ht))
            for (var i = 0; i < Math.Min(ht.GetArrayLength(), 168); i++)
                if (Instant(At(hourly, "time", i)) is { } time)
                    hours.Add(new(time, At(hourly, "temperature_2m", i), (int?)At(hourly, "weather_code", i), At(hourly, "precipitation_probability", i)));
        var days = new List<WeatherDay>();
        if (root.TryGetProperty("daily", out var daily) && daily.TryGetProperty("time", out var dt))
            for (var i = 0; i < Math.Min(dt.GetArrayLength(), 7); i++)
                if (Instant(At(daily, "time", i)) is { } time)
                    days.Add(new(WeatherFormatting.LocalTime(time, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), At(daily, "temperature_2m_max", i), At(daily, "temperature_2m_min", i), (int?)At(daily, "weather_code", i), Instant(At(daily, "sunrise", i)), Instant(At(daily, "sunset", i))));
        return new() { Key = key, FetchedAt = fetched, ValidAt = Instant(Number(current, "time")) ?? throw new InvalidDataException("Weather time is missing."), TimeZone = zone,
            Temperature = Number(current, "temperature_2m"), FeelsLike = Number(current, "apparent_temperature"), Humidity = Number(current, "relative_humidity_2m"), Wind = Number(current, "wind_speed_10m"), WindDirection = Number(current, "wind_direction_10m"),
            Code = (int?)Number(current, "weather_code"), IsDay = Number(current, "is_day") != 0, Hourly = hours.ToArray(), Daily = days.ToArray() };
    }
}

public sealed class WeatherRateLimitException(TimeSpan retryAfter) : HttpRequestException("Weather provider rate limit.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

public static class WeatherCache
{
    public static string PathFor(string directory) => Path.Combine(directory, "weather-cache.json");
    public static async Task<WeatherSnapshot[]> ReadAsync(string directory, CancellationToken token = default)
    {
        try
        {
            var path = PathFor(directory);
            if (!File.Exists(path) || new FileInfo(path).Length > 4_000_000) return [];
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
            return (await JsonSerializer.DeserializeAsync<WeatherSnapshot[]>(stream, cancellationToken: token) ?? [])
                .Where(s => s is not null && s.Key is { Length: <= 64 } && s.Hourly is not null && s.Daily is not null && s.TimeZone is { Length: <= 100 })
                .Take(32).Select(s => s with { Hourly = s.Hourly.Where(h => h is not null).Take(168).ToArray(), Daily = s.Daily.Where(d => d is not null && DateTime.TryParseExact(d.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)).Take(7).ToArray() }).ToArray();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }
    public static async Task WriteAsync(string directory, IEnumerable<WeatherSnapshot> snapshots, CancellationToken token)
    {
        var path = PathFor(directory); var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshots.Take(32)), token);
        File.Move(temp, path, true);
    }
}
