using System.Globalization;

namespace RTSPView.Core;

public sealed record WeatherOptions
{
    public string Location { get; init; } = "Home";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string TimeZone { get; init; } = "auto";
    public string Preset { get; init; } = "compact";
    public string Units { get; init; } = "imperial";
    public string Theme { get; init; } = "auto";
    public string Accent { get; init; } = "#F2C75C";
    public int BackgroundOpacity { get; init; } = 80;
    public int FontSize { get; init; } = 24;
    public int IconSize { get; init; } = 36;
    public int Padding { get; init; } = 14;
    public int CornerRadius { get; init; } = 10;
    public string Alignment { get; init; } = "left";
    public string[] Fields { get; init; } = ["location", "temperature", "condition", "highLow"];
    public static readonly string[] AllowedFields = ["location", "temperature", "condition", "highLow", "feelsLike", "humidity", "wind", "precipitation", "sun", "clock", "hourly", "daily"];
    public string CacheKey => Latitude.ToString("F4", CultureInfo.InvariantCulture) + "," + Longitude.ToString("F4", CultureInfo.InvariantCulture);
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Location) || Location.Length > 80 || !double.IsFinite(Latitude) || !double.IsFinite(Longitude) || Latitude is < -90 or > 90 || Longitude is < -180 or > 180 ||
            TimeZone is null || TimeZone.Length > 100 || Preset is not ("minimal" or "compact" or "overlay" or "detailed" or "forecast" or "dashboard") || Units is not ("metric" or "imperial") ||
            Theme is not ("auto" or "dark" or "light") || Alignment is not ("left" or "center" or "right") ||
            Accent is not { Length: 7 } || Accent[0] != '#' || Accent.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") >= 0 ||
            BackgroundOpacity is < 0 or > 100 || FontSize is < 12 or > 64 || IconSize is < 16 or > 96 || Padding is < 0 or > 48 || CornerRadius is < 0 or > 48 ||
            Fields is null || Fields.Length > AllowedFields.Length || Fields.Any(f => !AllowedFields.Contains(f)) || Fields.Distinct().Count() != Fields.Length)
            throw new InvalidDataException("Weather settings contain an invalid location, appearance, or field selection.");
    }
}

public sealed record WeatherOverlay
{
    public int HostCameraSlot { get; init; }
    public bool Enabled { get; init; } = true;
    public int WidthPercent { get; init; } = 40;
    public int X { get; init; }
    public int Y { get; init; } = 100;
    public int Margin { get; init; } = 12;
    public WeatherOptions Weather { get; init; } = new() { Preset = "overlay" };
    public void Validate()
    {
        if (!AppSettings.MainCameraSlots.Contains(HostCameraSlot) || WidthPercent is < 15 or > 95 || X is < 0 or > 100 || Y is < 0 or > 100 || Margin is < 0 or > 80 || Weather is null)
            throw new InvalidDataException("Choose a camera and valid weather overlay position.");
        Weather.Validate();
    }
}

public sealed record WeatherHour(DateTimeOffset Time, double? Temperature, int? Code, double? RainChance);
public sealed record WeatherDay(string Date, double? High, double? Low, int? Code, DateTimeOffset? Sunrise, DateTimeOffset? Sunset);
public sealed record WeatherSnapshot
{
    public string Key { get; init; } = "";
    public string TimeZone { get; init; } = "UTC";
    public DateTimeOffset FetchedAt { get; init; }
    public DateTimeOffset ValidAt { get; init; }
    public bool RefreshFailed { get; init; }
    public double? Temperature { get; init; }
    public double? FeelsLike { get; init; }
    public double? Humidity { get; init; }
    public double? Wind { get; init; }
    public double? WindDirection { get; init; }
    public int? Code { get; init; }
    public bool IsDay { get; init; } = true;
    public WeatherHour[] Hourly { get; init; } = [];
    public WeatherDay[] Daily { get; init; } = [];
    public string Freshness(DateTimeOffset now) => FetchedAt == default ? "unavailable" : now - FetchedAt > TimeSpan.FromHours(6) || now - ValidAt > TimeSpan.FromHours(6) ? "unavailable" : now - FetchedAt > TimeSpan.FromMinutes(45) || now - ValidAt > TimeSpan.FromMinutes(60) ? "stale" : RefreshFailed ? "retrying" : "fresh";
}

public static class WeatherFormatting
{
    public static string Temperature(double? celsius, string units) => celsius.HasValue ? Math.Round(units == "imperial" ? celsius.Value * 1.8 + 32 : celsius.Value).ToString(CultureInfo.InvariantCulture) + "°" : "—";
    public static string Condition(int? code) => code switch { 0 => "Clear", 1 => "Mostly clear", 2 => "Partly cloudy", 3 => "Overcast", 45 or 48 => "Fog", >= 51 and <= 57 => "Drizzle", >= 61 and <= 67 => "Rain", >= 71 and <= 77 => "Snow", >= 80 and <= 82 => "Rain showers", 85 or 86 => "Snow showers", >= 95 and <= 99 => "Thunderstorms", _ => "Conditions unavailable" };
    public static string Icon(int? code, bool day = true) => code switch { 0 => day ? "☀" : "☾", 1 or 2 => "⛅", 3 or 45 or 48 => "☁", >= 71 and <= 77 or 85 or 86 => "❄", >= 95 and <= 99 => "ϟ", >= 51 and <= 82 => "☂", _ => "◇" };
    public static DateTimeOffset LocalTime(DateTimeOffset time, string zone)
    {
        try { return TimeZoneInfo.ConvertTime(time, TimeZoneInfo.FindSystemTimeZoneById(zone)); }
        catch (TimeZoneNotFoundException) { return time; }
        catch (InvalidTimeZoneException) { return time; }
    }
}

public static class WeatherConfiguration
{
    public static bool OnlyPresentationChanged(AppSettings previous, AppSettings updated)
    {
        static string Signature(AppSettings settings) => System.Text.Json.JsonSerializer.Serialize(settings with
        {
            WeatherOverlays = [],
            Layouts = settings.Layouts.Select(layout => layout with
            {
                Tiles = layout.Tiles.Select(tile => tile with { Weather = null }).ToArray()
            }).ToArray()
        });
        return Signature(previous) == Signature(updated);
    }
}

public static class WeatherGeometry
{
    public static (double Width, double Height, double Left, double Top) Bounds(WeatherOverlay overlay, double width, double height)
    {
        var o = overlay.Weather;
        var margin = Math.Min(overlay.Margin, Math.Min(width / 2, height / 2));
        var available = Math.Max(0, width - margin * 2);
        var availableHeight = Math.Max(0, height - margin * 2);
        var w = available * overlay.WidthPercent / 100;
        if (o.Preset == "minimal") w = Math.Min(w, Math.Max(160, o.FontSize * 4 + o.IconSize + 10) + o.Padding * 2);
        var size = Math.Min(o.FontSize, Math.Max(12, (w - o.Padding * 2) / 5));
        bool Has(string field) => o.Fields.Contains(field);
        double h = o.Padding * 2 + 24;
        if (Has("location")) h += Math.Max(12, size * .6) * 1.4 + 2;
        if (Has("temperature") || Has("condition")) h += Math.Max(size * 1.5, Math.Min(o.IconSize, Math.Max(14, (w - o.Padding * 2) * .2))) * 1.4 + 2;
        if (Has("condition") && o.Preset != "minimal") h += Math.Max(12, size * .65) * 1.4 + 2;
        if (Has("highLow")) h += Math.Max(12, size * .6) * 1.4 + 2;
        h += new[] { "feelsLike", "humidity", "wind", "precipitation", "sun", "clock" }.Count(Has) * (Math.Max(12, size * .55) * 1.4 + 2);
        if (Has("hourly")) h = Math.Max(h + 90, 320 + o.Padding * 2);
        if (Has("daily")) h = Math.Max(h + 140, 480 + o.Padding * 2);
        h = Math.Min(availableHeight, Math.Ceiling(h));
        return (w, h, margin + (available - w) * overlay.X / 100, margin + (availableHeight - h) * overlay.Y / 100);
    }
}
