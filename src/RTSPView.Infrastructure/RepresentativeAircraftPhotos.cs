using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

public static class RepresentativeAircraftPhotos
{
    private static readonly Dictionary<string,string> Models = new(StringComparer.OrdinalIgnoreCase)
    {
        ["B738"]="Boeing 737-800", ["B739"]="Boeing 737-900", ["B38M"]="Boeing 737 MAX 8", ["B39M"]="Boeing 737 MAX 9",
        ["B744"]="Boeing 747-400", ["B748"]="Boeing 747-8", ["A320"]="Airbus A320", ["A321"]="Airbus A321",
        ["C172"]="Cessna 172", ["C182"]="Cessna 182", ["R182"]="Cessna R182"
    };
    public static string? Key(AircraftTrack track)
    {
        var model = Models.GetValueOrDefault(track.Type, track.Type).Trim();
        // Unknown short ICAO codes do not identify a model reliably in image search.
        if (model.Length < 5 || model.Length > 160 || !model.Any(char.IsDigit)) return null;
        var airline = track.Airline.Trim();
        return "model/" + Uri.EscapeDataString(model) + "|" + Uri.EscapeDataString(airline);
    }
    public static (string Model, string Airline) Identity(string key)
    {
        var parts = key[6..].Split('|'); return (Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1]));
    }
    public static string Url(string key)
    {
        var (model, airline) = Identity(key);
        var query = model.Replace('"',' ') + " " + airline.Replace('"',' ') + " filetype:bitmap";
        return "https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=6&gsrlimit=5&gsrsearch=" + Uri.EscapeDataString(query) + "&prop=imageinfo&iiprop=url%7Cextmetadata&iiurlwidth=480";
    }
    private static string Plain(string text) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", " ")), @"\s+", " ").Trim();
    private static string Compact(string text) => new(text.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    public static AircraftPhoto? Parse(string json, string key)
    {
        using var doc = JsonDocument.Parse(json);
        static JsonElement Child(JsonElement e,string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name,out var v) ? v : default;
        static string Text(JsonElement e,string name) => Child(e,name) is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : "";
        var pages = Child(Child(doc.RootElement,"query"),"pages"); if(pages.ValueKind != JsonValueKind.Object) return null;
        var (model,airline) = Identity(key);
        foreach(var page in pages.EnumerateObject())
        {
            var infos = Child(page.Value,"imageinfo"); if(infos.ValueKind != JsonValueKind.Array || infos.GetArrayLength()==0) continue;
            var info = infos[0]; var meta = Child(info,"extmetadata");
            string Value(string name) => Plain(Text(Child(meta,name),"value"));
            var description = Compact(Text(page.Value,"title") + " " + Value("ObjectName") + " " + Value("ImageDescription"));
            if(!description.Contains(Compact(model)) || (airline.Length>0 && !description.Contains(Compact(airline)))) continue;
            var license = Value("LicenseShortName"); var artist = Value("Artist");
            if(!(license.StartsWith("CC BY") || license is "CC0" or "Public domain") || artist.Length is 0 or >160) continue;
            var photo = new AircraftPhoto(Text(info,"thumburl"),Text(info,"descriptionurl"),artist)
                { Representative = true, Source = "Wikimedia Commons", License = license };
            if(photo.IsValid) return photo;
        }
        return null;
    }
}
