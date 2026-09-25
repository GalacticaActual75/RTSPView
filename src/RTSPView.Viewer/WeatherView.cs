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
        var o = _options; var s = _snapshot; var now = DateTimeOffset.UtcNow;
        var light = o.Theme == "light";
        WidgetAppearance.Apply(this, o);
        var foreground = light ? Brushes.Black : Brushes.White;
        var muted = new SolidColorBrush(light ? Color.FromRgb(70, 80, 95) : Color.FromRgb(160, 174, 190));
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition());
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(stack);
        var credit = new TextBlock { Text = "Open-Meteo.com · CC BY 4.0", FontSize = 10, Foreground = muted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 3, 0, 0) };
        Grid.SetRow(credit, 1); content.Children.Add(credit); Child = content;
        var size = Math.Min(o.FontSize, Math.Max(12, (ActualWidth - o.Padding * 2) / 5));
        var width = Math.Max(0, ActualWidth - o.Padding * 2); var height = Math.Max(0, ActualHeight - o.Padding * 2 - 18);
        bool Has(string field) => o.Fields.Contains(field);
        void Text(string text, double font, bool secondary = false)
        {
            stack.Children.Add(new TextBlock { Text = text, FontFamily = new("Segoe UI"), FontSize = font, Foreground = secondary ? muted : foreground,
                TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = o.Alignment switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left }, Margin = new(0, 1, 0, 1) });
        }
        if (Has("location")) Text(o.Location, Math.Max(12, size * .6), true);
        var freshness = s?.Freshness(now) ?? "unavailable";
        // Status belongs in the persistent footer so small cards cannot hide stale data warnings.
        if (freshness is "stale" or "retrying") credit.Text = (freshness == "stale" ? "Outdated" : "Refresh failed") + " · Open-Meteo.com · CC BY 4.0";
        var omitted = s is not null && ((Has("hourly") && (width < 240 || height < 280)) || (Has("daily") && (width < 240 || height < 380)));
        if (freshness == "unavailable") Text(s is null ? "No weather fetched yet" : "Weather unavailable", Math.Max(12, size * .65));
        else
        {
            var today = s!.Daily.FirstOrDefault(d => d.Date == WeatherFormatting.LocalTime(now, s.TimeZone).ToString("yyyy-MM-dd"));
            if (Has("temperature") || Has("condition"))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = o.Alignment switch { "center" => HorizontalAlignment.Center, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Left } };
                if (Has("condition")) row.Children.Add(new TextBlock { Text = WeatherFormatting.Icon(s.Code, s.IsDay), FontSize = Math.Min(o.IconSize, Math.Max(14, width * .2)), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(o.Accent)), Margin = new(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                if (Has("temperature")) row.Children.Add(new TextBlock { Text = WeatherFormatting.Temperature(s.Temperature, o.Units) + (o.Units == "imperial" ? "F" : "C"), FontSize = size * 1.5, FontWeight = FontWeights.SemiBold, Foreground = foreground });
                stack.Children.Add(row);
            }
            if (Has("condition") && o.Preset != "minimal") Text(WeatherFormatting.Condition(s.Code), Math.Max(12, size * .65));
            if (Has("highLow")) Text("H " + WeatherFormatting.Temperature(today?.High, o.Units) + "  L " + WeatherFormatting.Temperature(today?.Low, o.Units), Math.Max(12, size * .6), true);
            if (height > 0)
            {
                var metrics = new List<string>();
                if (Has("feelsLike")) metrics.Add("Feels like " + WeatherFormatting.Temperature(s.FeelsLike, o.Units));
                if (Has("humidity")) metrics.Add("Humidity " + (s.Humidity?.ToString("0") ?? "—") + "%");
                if (Has("wind")) metrics.Add("Wind " + (s.Wind is { } wind ? (wind * (o.Units == "imperial" ? 2.236936 : 3.6)).ToString("0") : "—") + (o.Units == "imperial" ? " mph" : " km/h") + (s.WindDirection is { } direction ? " · " + new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[((int)Math.Round(direction / 45) % 8 + 8) % 8] : ""));
                var hour = s.Hourly.FirstOrDefault(h => h.Time <= now && h.Time.AddHours(1) > now);
                if (Has("precipitation")) metrics.Add("Rain chance " + (hour?.RainChance?.ToString("0") ?? "—") + "%");
                if (Has("sun")) metrics.Add("Rise " + (today?.Sunrise is { } rise ? WeatherFormatting.LocalTime(rise, s.TimeZone).ToString("HH:mm") : "—") + " · Set " + (today?.Sunset is { } set ? WeatherFormatting.LocalTime(set, s.TimeZone).ToString("HH:mm") : "—"));
                foreach (var metric in metrics) Text(metric, Math.Max(12, size * .55), true);
            }
            if (width >= 240 && height >= 280 && Has("hourly"))
            {
                var row = new UniformGrid { Columns = Math.Clamp((int)(width / 80), 2, 6), Margin = new(0, 8, 0, 0) };
                omitted |= Math.Min(6, s.Hourly.Count(h => h.Time >= now)) > row.Columns;
                foreach (var hour in s.Hourly.Where(h => h.Time >= now).Take(row.Columns)) row.Children.Add(new TextBlock { Text = WeatherFormatting.LocalTime(hour.Time, s.TimeZone).ToString("HH:mm") + "\n" + WeatherFormatting.Icon(hour.Code) + "\n" + WeatherFormatting.Temperature(hour.Temperature, o.Units), Foreground = foreground, FontSize = Math.Max(12, size * .6), TextAlignment = TextAlignment.Center });
                stack.Children.Add(row);
            }
            if (width >= 240 && height >= 380 && Has("daily"))
            {
                omitted |= Math.Min(5, s.Daily.Count(d => string.CompareOrdinal(d.Date, WeatherFormatting.LocalTime(now, s.TimeZone).ToString("yyyy-MM-dd")) > 0)) > Math.Clamp((int)((height - 350) / 28), 1, 5);
                foreach (var day in s.Daily.Where(d => string.CompareOrdinal(d.Date, WeatherFormatting.LocalTime(now, s.TimeZone).ToString("yyyy-MM-dd")) > 0).Take(Math.Clamp((int)((height - 350) / 28), 1, 5)))
                    Text(DateTime.ParseExact(day.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToString("ddd") + "   " + WeatherFormatting.Icon(day.Code) + "   " + WeatherFormatting.Temperature(day.High, o.Units) + " / " + WeatherFormatting.Temperature(day.Low, o.Units), Math.Max(12, size * .6), true);
            }
            if (freshness != "fresh") Text((freshness == "stale" ? "Outdated" : "Refresh failed") + " · " + Math.Max(0, (int)(now - s.FetchedAt).TotalMinutes) + "m ago", 11, true);
        }
        if (Has("clock")) Text(WeatherFormatting.LocalTime(now, s?.TimeZone ?? (o.TimeZone == "auto" ? "UTC" : o.TimeZone)).ToString("ddd, MMM d · HH:mm"), Math.Max(12, size * .55), true);
        stack.Measure(new System.Windows.Size(width, double.PositiveInfinity));
        while (stack.DesiredSize.Height > height && stack.Children.Count > 1)
        {
            // Remove lowest-priority trailing detail before shrinking the main reading.
            omitted = true;
            stack.Children.RemoveAt(stack.Children.Count - 1);
            stack.Measure(new System.Windows.Size(width, double.PositiveInfinity));
        }
        if (omitted) credit.Text = "Details hidden · " + credit.Text;
        System.Windows.Automation.AutomationProperties.SetHelpText(this, omitted ? "Some selected weather details do not fit. Enlarge the tile or widget." : "");
    }
}
