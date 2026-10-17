namespace RTSPView.Core;

public sealed record WeatherCondition(string Label, string Scene, int Intensity);
public static class WeatherConditions
{
    public static readonly IReadOnlyDictionary<int, WeatherCondition> All = new Dictionary<int, WeatherCondition>
    {
        [0] = new("Clear sky", "clear", 0),
        [1] = new("Mainly clear", "partly", 1),
        [2] = new("Partly cloudy", "partly", 2),
        [3] = new("Overcast", "cloud", 3),
        [45] = new("Fog", "fog", 1),
        [48] = new("Depositing rime fog", "fog", 2),
        [51] = new("Light drizzle", "rain", 1),
        [53] = new("Moderate drizzle", "rain", 2),
        [55] = new("Dense drizzle", "rain", 3),
        [56] = new("Light freezing drizzle", "ice", 1),
        [57] = new("Dense freezing drizzle", "ice", 3),
        [61] = new("Slight rain", "rain", 1),
        [63] = new("Moderate rain", "rain", 2),
        [65] = new("Heavy rain", "rain", 3),
        [66] = new("Light freezing rain", "ice", 1),
        [67] = new("Heavy freezing rain", "ice", 3),
        [71] = new("Slight snowfall", "snow", 1),
        [73] = new("Moderate snowfall", "snow", 2),
        [75] = new("Heavy snowfall", "snow", 3),
        [77] = new("Snow grains", "snow", 1),
        [80] = new("Slight rain showers", "rain", 1),
        [81] = new("Moderate rain showers", "rain", 2),
        [82] = new("Violent rain showers", "rain", 3),
        [85] = new("Slight snow showers", "snow", 1),
        [86] = new("Heavy snow showers", "snow", 3),
        [95] = new("Thunderstorm", "storm", 2),
        [96] = new("Thunderstorm with slight hail", "hail", 2),
        [97] = new("Heavy thunderstorm", "storm", 3),
        [99] = new("Thunderstorm with heavy hail", "hail", 3),
    };
    public static WeatherCondition For(int? code) => code.HasValue && All.TryGetValue(code.Value, out var value) ? value : new("Conditions unavailable", "none", 0);
}
