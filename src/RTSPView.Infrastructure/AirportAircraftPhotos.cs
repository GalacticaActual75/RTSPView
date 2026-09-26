using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

// Documented thumbnail API: https://airport-data.com/api/doc/
public static class AirportAircraftPhotos
{
    public static string Url(string key)
    {
        var parts = key.Split('/');
        return "https://airport-data.com/api/ac_thumb.json?m=" + Uri.EscapeDataString(parts[1]) + "&n=1" +
            (parts.Length > 2 && parts[2].Length > 0 ? "&r=" + Uri.EscapeDataString(parts[2]) : "");
    }
    public static AircraftPhoto? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("status", out var status) && (status.ValueKind == JsonValueKind.Number ? status.TryGetInt32(out var code) : throw new JsonException("Invalid photo API status")) && code != 200)
        { if (code == 404) return null; throw new HttpRequestException("Airport-Data photo lookup failed: " + code); }
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string Text(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            var photo = new AircraftPhoto(Text("image"), Text("link"), Text("photographer")) { Source = "Airport-Data.com" };
            if (photo.IsValid) return photo;
        }
        return null;
    }
}
