namespace RTSPView.Core;

// Shared by the display and representative photo lookup. Unknown types remain unchanged.
public static class AircraftModels
{
    private static readonly Dictionary<string,string> Models = new(StringComparer.OrdinalIgnoreCase)
    {
        ["B738"]="Boeing 737-800", ["B739"]="Boeing 737-900", ["B38M"]="Boeing 737 MAX 8", ["B39M"]="Boeing 737 MAX 9",
        ["B744"]="Boeing 747-400", ["B748"]="Boeing 747-8", ["A320"]="Airbus A320", ["A321"]="Airbus A321",
        ["C172"]="Cessna 172", ["C182"]="Cessna 182", ["R182"]="Cessna R182",
        ["SR20"]="Cirrus SR20", ["SR22"]="Cirrus SR22", ["SR22T"]="Cirrus SR22T",
        ["PA-32R-301T"]="Piper PA-32R-301T", ["PA-28-181"]="Piper PA-28-181"
    };
    public static string Name(string? type) => Models.GetValueOrDefault(type?.Trim() ?? "", type?.Trim() ?? "");
}
