using System.Globalization;

namespace RTSPView.Core;

public sealed record SystemStatsOptions
{
    public WeatherOptions Appearance { get; init; } = new();
    public string TemperatureUnit { get; init; } = "celsius";
    public bool ShowHeading { get; init; } = true;
    public void Validate()
    {
        if (Appearance is null || TemperatureUnit is not ("celsius" or "fahrenheit")) throw new InvalidDataException("Choose a valid temperature unit and appearance.");
        Appearance.Validate();
    }
    public static bool IsFresh(SystemTelemetry? sample, DateTimeOffset now) => sample is not null && now - sample.Timestamp < TimeSpan.FromSeconds(10) && sample.Timestamp <= now.AddSeconds(5);
    public string Temperature(double? value) => value is { } v && double.IsFinite(v) ? (TemperatureUnit == "fahrenheit" ? v * 1.8 + 32 : v).ToString("0", CultureInfo.InvariantCulture) + (TemperatureUnit == "fahrenheit" ? "°F" : "°C") : "—";
}

public sealed record DateTimeOptions
{
    public WeatherOptions Appearance { get; init; } = new();
    public string TimeZone { get; init; } = "auto";
    public bool Use24Hour { get; init; }
    public bool ShowSeconds { get; init; } = true;
    public bool ShowDate { get; init; } = true;
    public bool ShowWeekday { get; init; } = true;
    public string DateFormat { get; init; } = "long";
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeZoneInfo Zone => TimeZone == "auto" ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
    public void Validate()
    {
        if (Appearance is null || string.IsNullOrWhiteSpace(TimeZone) || TimeZone.Length > 128 || DateFormat is not ("long" or "short" or "iso")) throw new InvalidDataException("Choose a valid clock format and time zone.");
        Appearance.Validate();
        try { _ = Zone; } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new InvalidDataException("Choose a supported time zone."); }
    }
    public (string Time, string Date) Format(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Zone);
        var time = local.ToString((Use24Hour ? "HH:mm" : "h:mm") + (ShowSeconds ? ":ss" : "") + (Use24Hour ? "" : " tt"), CultureInfo.InvariantCulture);
        var date = ShowDate ? local.ToString(DateFormat switch { "iso" => "yyyy-MM-dd", "short" => "MMM d, yyyy", _ => "MMMM d, yyyy" }, CultureInfo.InvariantCulture) : "";
        if (ShowWeekday) date = local.ToString("dddd", CultureInfo.InvariantCulture) + (date.Length > 0 ? " · " + date : "");
        return (time, date);
    }
}
