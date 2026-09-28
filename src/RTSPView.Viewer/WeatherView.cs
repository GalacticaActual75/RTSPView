using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RTSPView.Core;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using ColorConverter = System.Windows.Media.ColorConverter;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace RTSPView.Viewer;

// Pure WPF content: no browser, video callbacks, network calls or animations.
public sealed class WeatherView : Border
{
    public bool IndependentWidget { get; set; }
    private WeatherOptions _options = new();
    private WeatherSnapshot? _snapshot;
    private string _signature = "";
    public WeatherView() { ClipToBounds = true; IsHitTestVisible = false; SizeChanged += (_, _) => Render(); }
    public void Update(WeatherOptions options, WeatherSnapshot? snapshot)
    {
        var signature = System.Text.Json.JsonSerializer.Serialize(options) + snapshot?.FetchedAt + snapshot?.RefreshFailed + DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        if (signature == _signature) return;
        _signature = signature; _options = options; _snapshot = snapshot; Render();
    }
    private void Render()
    {
        var original = _options; var snapshot = _snapshot; var now = DateTimeOffset.UtcNow;
        var viewport = WidgetViewport.WeatherFor(ActualWidth, ActualHeight, original.Density);
        var o = original with { Padding = (int)viewport.Padding };
        WidgetAppearance.Apply(this, o);
        var flow = new WidgetFlow(); Child = flow;
        var primary = flow;
        var columns = viewport.Wide && original.Fields.Contains("temperature") && original.Fields.Any(field => field is "location" or "highLow" or "feelsLike" or "humidity" or "wind" or "precipitation" or "sun") && snapshot?.Freshness(now) != "unavailable" && snapshot is not null;
        if (columns)
        {
            Child = null;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(48, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(48, GridUnitType.Star) });
            primary = new WidgetFlow(); grid.Children.Add(primary); grid.Children.Add(flow); Grid.SetColumn(flow, 2); Child = grid;
        }
        Child.Opacity = o.ContentOpacity / 100d;
        var primaryOptions = o;
        bool Has(string field) => o.Fields.Contains(field);
        void Line(string text, double size, int priority) => flow.Add(WidgetFlow.Text(text, size, o), priority);
        var state = snapshot?.Freshness(now) ?? "unavailable";
        if (Has("location") && viewport.DetailLevel > 0)
        {
            var location = WidgetFlow.Text(o.Location, columns ? viewport.Heading : viewport.Font, o);
            if (columns) location.FontWeight = FontWeights.SemiBold;
            flow.Add(location, 40);
        }
        if (state == "unavailable") Line(snapshot is null ? "Waiting for weather" : "Weather unavailable", viewport.Heading, 100);
        else
        {
            var s = snapshot!;
            var today = s.Daily.FirstOrDefault(d => d.Date == WeatherFormatting.LocalTime(now, s.TimeZone).ToString("yyyy-MM-dd"));
            if (Has("temperature"))
            {
                var reading = WidgetFlow.Text(WeatherFormatting.Temperature(s.Temperature, o.Units) + (o.Units == "imperial" ? "F" : "C"), viewport.Reading, primaryOptions);
                reading.FontWeight = FontWeights.SemiBold; primary.Add(reading, 100);
            }
            if (Has("highLow")) Line("H " + WeatherFormatting.Temperature(today?.High, o.Units) + "   L " + WeatherFormatting.Temperature(today?.Low, o.Units), viewport.Font, 90);
            if (Has("condition"))
            {
                var condition = WidgetFlow.Text(WeatherFormatting.Icon(s.Code, s.IsDay) + " " + WeatherFormatting.Condition(s.Code), viewport.Font, primaryOptions);
                condition.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(o.Accent)); primary.Add(condition, 80);
            }
            if (viewport.DetailLevel > 0)
            {
                if (Has("feelsLike")) Line("Feels like " + WeatherFormatting.Temperature(s.FeelsLike, o.Units), viewport.Font, 35);
                if (Has("wind")) Line("Wind " + (s.Wind is {} wind ? (wind * (o.Units == "imperial" ? 2.236936 : 3.6)).ToString("0") : "—") + (o.Units == "imperial" ? " mph" : " km/h") + (s.WindDirection is {} direction ? " · " + AircraftSelection.Cardinal(direction) : ""), viewport.Font, 34);
                if (Has("humidity")) Line("Humidity " + (s.Humidity?.ToString("0") ?? "—") + "%", viewport.Font, 33);
                if (Has("precipitation")) Line("Rain chance " + (s.Hourly.FirstOrDefault(h => h.Time <= now && h.Time.AddHours(1) > now)?.RainChance?.ToString("0") ?? "—") + "%", viewport.Font, 32);
                if (Has("sun")) Line("Rise " + (today?.Sunrise is {} rise ? WeatherFormatting.LocalTime(rise, s.TimeZone).ToString("HH:mm") : "—") + " · Set " + (today?.Sunset is {} set ? WeatherFormatting.LocalTime(set, s.TimeZone).ToString("HH:mm") : "—"), viewport.Font, 31);
            }
            if (viewport.DetailLevel > 1)
            {
                if (Has("hourly")) foreach (var hour in s.Hourly.Where(h => h.Time >= now).Take(4)) Line(WeatherFormatting.LocalTime(hour.Time, s.TimeZone).ToString("HH:mm") + "  " + WeatherFormatting.Icon(hour.Code) + "  " + WeatherFormatting.Temperature(hour.Temperature, o.Units), viewport.Font, 20);
                if (Has("daily")) foreach (var day in s.Daily.Where(d => string.CompareOrdinal(d.Date, WeatherFormatting.LocalTime(now, s.TimeZone).ToString("yyyy-MM-dd")) > 0).Take(3)) Line(day.Date + "  " + WeatherFormatting.Temperature(day.High, o.Units) + " / " + WeatherFormatting.Temperature(day.Low, o.Units), viewport.Font, 19);
            }
        }
        if (Has("clock") && viewport.DetailLevel > 0) Line(WeatherFormatting.LocalTime(now, snapshot?.TimeZone ?? o.TimeZone).ToString("ddd, MMM d · HH:mm"), viewport.Font, 30);
        var warning = state is "stale" or "retrying" ? (state == "stale" ? "Outdated · " : "Refresh failed · ") : "";
        Line(warning + "Open-Meteo.com · CC BY 4.0", Math.Max(10, viewport.Font * .75), 95);

    }
}
