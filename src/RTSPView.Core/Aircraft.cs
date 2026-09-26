using System.Globalization;

namespace RTSPView.Core;

public sealed record AircraftOptions
{
    public string Location { get; init; } = "Home";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double RadiusMiles { get; init; } = 10;
    public int? MinimumAltitudeFeet { get; init; }
    public int? MaximumAltitudeFeet { get; init; }
    public string Preset { get; init; } = "featured";
    public string Units { get; init; } = "imperial";
    public int MaximumAircraft { get; init; } = 5;
    // Optional for widgets; permanent tiles remain visible and camera cards use traffic eligibility.
    public bool HideWhenEmpty { get; init; }
    public bool ShowPhoto { get; init; } = true;
    public string Theme { get; init; } = "auto";
    public string Accent { get; init; } = "#F2C75C";
    public int BackgroundOpacity { get; init; } = 80;
    public int FontSize { get; init; } = 24;
    public int IconSize { get; init; } = 36;
    public int Padding { get; init; } = 14;
    public int CornerRadius { get; init; } = 10;
    public string Alignment { get; init; } = "left";
    public string[] Fields { get; init; } = ["type", "owner", "airline", "destination", "altitude", "speed", "distance", "track", "verticalRate"];
    public static readonly string[] AllowedFields = ["type", "owner", "airline", "destination", "altitude", "speed", "distance", "track", "verticalRate"];
    // Filters and presentation share one query for the same area.
    public string CacheKey => FormattableString.Invariant($"{Latitude:F4},{Longitude:F4},{RadiusMiles:F1}");
    [System.Text.Json.Serialization.JsonIgnore]
    public WeatherOptions Appearance => new() { Theme = Theme, Accent = Accent, BackgroundOpacity = BackgroundOpacity,
        FontSize = FontSize, IconSize = IconSize, Padding = Padding, CornerRadius = CornerRadius, Alignment = Alignment };
    public void Validate()
    {
        Appearance.Validate();
        if (string.IsNullOrWhiteSpace(Location) || Location.Length > 80 || !double.IsFinite(Latitude) || !double.IsFinite(Longitude) ||
            Latitude is < -90 or > 90 || Longitude is < -180 or > 180 || !double.IsFinite(RadiusMiles) || RadiusMiles is < 1 or > 100 ||
            MinimumAltitudeFeet is < -2000 or > 100000 || MaximumAltitudeFeet is < -2000 or > 100000 || MinimumAltitudeFeet > MaximumAltitudeFeet ||
            Preset is not ("featured" or "board") || Units is not ("imperial" or "metric") || MaximumAircraft is < 1 or > 5 ||
            Fields is null || Fields.Length > AllowedFields.Length || Fields.Any(f => !AllowedFields.Contains(f)) || Fields.Distinct().Count() != Fields.Length)
            throw new InvalidDataException("Choose a valid aircraft location, radius, altitude range, display style, and fields.");
    }
}

public sealed record AircraftOverlay
{
    public int HostCameraSlot { get; init; }
    public bool Enabled { get; init; } = true;
    public int WidthPercent { get; init; } = 40;
    public int X { get; init; } = 100;
    public int Y { get; init; } = 100;
    public int Margin { get; init; } = 12;
    public AircraftOptions Aircraft { get; init; } = new() { Fields = ["type", "altitude", "speed", "distance"] };
    public void Validate()
    {
        if (Aircraft is null) throw new InvalidDataException("Aircraft settings are required.");
        new WeatherOverlay { HostCameraSlot = HostCameraSlot, Enabled = Enabled, WidthPercent = WidthPercent, X = X, Y = Y, Margin = Margin }.Validate();
        Aircraft.Validate();
    }
}

public sealed record AircraftTrack
{
    public AircraftPhoto? Photo { get; init; }
    public string RegisteredOwner { get; init; } = "";
    public string Airline { get; init; } = "";
    public string Destination { get; init; } = "";
    public string Hex { get; init; } = "";
    public string Callsign { get; init; } = "";
    public string Registration { get; init; } = "";
    public string Type { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double? AltitudeFeet { get; init; }
    public double? SpeedKnots { get; init; }
    public double? TrackDegrees { get; init; }
    public double? VerticalRate { get; init; }
    public DateTimeOffset PositionAt { get; init; }
    public string Label => !string.IsNullOrWhiteSpace(Callsign) ? Callsign : !string.IsNullOrWhiteSpace(Registration) ? Registration : Hex.ToUpperInvariant();
}

public sealed record AircraftSnapshot
{
    public string Key { get; init; } = "";
    public DateTimeOffset FetchedAt { get; init; }
    public bool RefreshFailed { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public AircraftTrack[] Aircraft { get; init; } = [];
    public string Freshness(DateTimeOffset now) => FetchedAt == default || now - FetchedAt > TimeSpan.FromSeconds(90) ? "unavailable" :
        RefreshFailed || now - FetchedAt > TimeSpan.FromSeconds(30) ? "stale" : "fresh";
}

public static class AircraftSelection
{
    public static bool ShouldReplaceCamera(AircraftOptions options, AircraftSnapshot? snapshot, DateTimeOffset now) =>
        snapshot?.Freshness(now) == "fresh" && Nearby(options, snapshot, now).Length > 0;
    public static double DistanceMiles(double lat, double lon, double targetLat, double targetLon)
    {
        const double radians = Math.PI / 180;
        var a = Math.Pow(Math.Sin((targetLat - lat) * radians / 2), 2) + Math.Cos(lat * radians) * Math.Cos(targetLat * radians) * Math.Pow(Math.Sin((targetLon - lon) * radians / 2), 2);
        return 3958.7613 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
    public static double Bearing(double lat, double lon, double targetLat, double targetLon)
    {
        const double r = Math.PI / 180;
        var d = (targetLon - lon) * r;
        return (Math.Atan2(Math.Sin(d) * Math.Cos(targetLat * r), Math.Cos(lat * r) * Math.Sin(targetLat * r) - Math.Sin(lat * r) * Math.Cos(targetLat * r) * Math.Cos(d)) / r + 360) % 360;
    }
    public static AircraftTrack[] Nearby(AircraftOptions options, AircraftSnapshot? snapshot, DateTimeOffset now) =>
        snapshot is null || snapshot.Freshness(now) == "unavailable" ? [] : snapshot.Aircraft
            .Where(a => a.PositionAt <= now.AddSeconds(5) && now - a.PositionAt <= TimeSpan.FromSeconds(60) &&
                DistanceMiles(options.Latitude, options.Longitude, a.Latitude, a.Longitude) <= options.RadiusMiles &&
                (!options.MinimumAltitudeFeet.HasValue || a.AltitudeFeet >= options.MinimumAltitudeFeet) &&
                (!options.MaximumAltitudeFeet.HasValue || a.AltitudeFeet <= options.MaximumAltitudeFeet))
            .OrderBy(a => DistanceMiles(options.Latitude, options.Longitude, a.Latitude, a.Longitude)).ThenBy(a => a.Hex, StringComparer.Ordinal).ToArray();
    public static string Cardinal(double degrees) => new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[(int)Math.Floor((degrees + 22.5) / 45) % 8];
    public static string Metric(string field, AircraftTrack a, AircraftOptions o)
    {
        static string Number(double? value, string format = "N0") => value?.ToString(format, CultureInfo.InvariantCulture) ?? "—";
        return field switch
        {
            "owner" => a.RegisteredOwner.Length > 0 ? a.RegisteredOwner : "Unavailable",
            "airline" => "Airline: " + (a.Airline.Length > 0 ? a.Airline : "Unavailable"),
            "destination" => "Destination (lookup): " + (a.Destination.Length > 0 ? a.Destination : "Unavailable"),
            "altitude" => "ALT " + Number(a.AltitudeFeet * (o.Units == "metric" ? .3048 : 1)) + (o.Units == "metric" ? " m" : " ft"),
            "speed" => "SPD " + Number(a.SpeedKnots * (o.Units == "metric" ? 1.852 : 1)) + (o.Units == "metric" ? " km/h" : " kt"),
            "track" => "TRK " + Number(a.TrackDegrees) + (a.TrackDegrees.HasValue ? "° " + Cardinal(a.TrackDegrees.Value) : ""),
            "verticalRate" => "V/S " + Number(a.VerticalRate * (o.Units == "metric" ? .00508 : 1), o.Units == "metric" ? "+0.0;-0.0;0" : "+0;-0;0") + (o.Units == "metric" ? " m/s" : " ft/min"),
            "distance" => Number(DistanceMiles(o.Latitude, o.Longitude, a.Latitude, a.Longitude) * (o.Units == "metric" ? 1.609344 : 1), "0.0") + (o.Units == "metric" ? " km " : " mi ") + Cardinal(Bearing(o.Latitude, o.Longitude, a.Latitude, a.Longitude)),
            "type" => string.Join(" · ", new[] { a.Type, a.Registration }.Where(s => !string.IsNullOrWhiteSpace(s))),
            _ => ""
        };
    }
}

public static class AircraftGeometry
{
    public static (double Width, double Height, double Left, double Top) Bounds(AircraftOverlay overlay, double width, double height)
    {
        var o = overlay.Aircraft;
        var margin = Math.Min(overlay.Margin, Math.Min(width / 2, height / 2));
        var w = Math.Max(0, width - margin * 2) * overlay.WidthPercent / 100;
        var size = Math.Min(o.FontSize, Math.Max(12, (w - o.Padding * 2) / 5));
        var rows = o.Preset == "board" ? o.MaximumAircraft : 1;
        var columns = w - o.Padding * 2 >= 400 ? 3 : w - o.Padding * 2 >= 220 ? 2 : 1;
        var lines = o.Fields.Count(f => f is "type" or "owner" or "airline" or "destination") + Math.Ceiling(o.Fields.Count(f => f is not ("type" or "owner" or "airline" or "destination")) / (double)columns);
        var h = Math.Min(Math.Max(0, height - margin * 2), Math.Ceiling(o.Padding * 2 + 42 + (o.ShowPhoto && w - o.Padding * 2 >= 180 ? 150 : 0) + rows * (Math.Max(size * 1.7, Math.Min(o.IconSize, (w - o.Padding * 2) * .2)) + 8 + lines * (Math.Max(12, size * .6) * 1.4 + 2))));
        return (w, h, margin + (Math.Max(0, width - margin * 2) - w) * overlay.X / 100, margin + (Math.Max(0, height - margin * 2) - h) * overlay.Y / 100);
    }
}
