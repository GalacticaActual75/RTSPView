using System.IO;
using Brushes = System.Windows.Media.Brushes;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Viewer;

public sealed class SystemWidgetView : Border
{
    private WallWidget _widget = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private SystemTelemetry? _sample;
    private DateTimeOffset _lastRead;
    public SystemWidgetView()
    {
        ClipToBounds = true;
        _timer.Tick += (_, _) => Draw();
        Loaded += (_, _) => { _timer.Start(); Draw(); };
        Unloaded += (_, _) => _timer.Stop();
        SizeChanged += (_, _) => Draw();
    }
    public void Update(WallWidget widget) { _widget = widget; Draw(); }
    private void Draw()
    {
        if (_widget.SystemStats is null && _widget.DateTime is null) return;
        var appearance = _widget.SystemStats?.Appearance ?? _widget.DateTime!.Appearance;
        WidgetAppearance.Apply(this, appearance with { Padding = (int)Math.Min(appearance.Padding, Math.Min(Width, Height) * .045) });
        var width = Math.Max(1, Width - Padding.Left - Padding.Right);
        var height = Math.Max(1, Height - Padding.Top - Padding.Bottom);
        var foreground = appearance.Theme == "light" ? Brushes.Black : Brushes.White;
        TextBlock Text(string value, double size, bool accent = false) => new() { Text = value, FontFamily = new("Segoe UI"), FontSize = size, Foreground = accent ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(appearance.Accent)) : foreground, TextAlignment = appearance.Alignment switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left }, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        if (_widget.DateTime is { } clock)
        {
            var (time, date) = clock.Format(DateTimeOffset.UtcNow);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(Text(time, Math.Clamp(Math.Min(width / (time.Length * .62), height * .48), 12, 180), true));
            if (date.Length > 0 && height >= 60) stack.Children.Add(Text(date, Math.Clamp(Math.Min(width / (date.Length * .55), height * .16), 10, 40)));
            stack.Opacity = appearance.ContentOpacity / 100d; Child = stack;
            return;
        }
        var options = _widget.SystemStats!;
        if (DateTimeOffset.UtcNow - _lastRead > TimeSpan.FromSeconds(2))
        {
            _lastRead = DateTimeOffset.UtcNow;
            try { _sample = JsonSerializer.Deserialize<SystemTelemetry>(File.ReadAllText(Path.Combine(AppPaths.DataDirectory, "system-stats.json"))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { _sample = null; }
        }
        var sample = SystemStatsOptions.IsFresh(_sample, DateTimeOffset.UtcNow) ? _sample : null;
        string Percent(double? v) => v is { } n && double.IsFinite(n) ? n.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%" : "—";
        var grid = new Grid();
        var heading = options.ShowHeading && height >= 120;
        if (heading) { grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.Children.Add(Text("System stats", Math.Clamp(height * .09, 11, 24))); }
        for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new());
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        var readings = new[] { ("CPU", Percent(sample?.CpuPercent)), ("GPU", Percent(sample?.GpuPercent)), ("RAM", Percent(sample?.RamPercent)), ("Memory used", sample?.RamUsedGb is { } ram ? ram.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB" : "—"), ("CPU temperature", options.Temperature(sample?.CpuTemperatureC)), ("GPU temperature", options.Temperature(sample?.GpuTemperatureC)) };
        for (var i = 0; i < readings.Length; i++)
        {
            var cell = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 1) };
            cell.Children.Add(Text(readings[i].Item1, Math.Clamp(Math.Min(width / 28, height / 16), 9, 24)));
            cell.Children.Add(Text(readings[i].Item2, Math.Clamp(Math.Min(width / 13, height / 9), 12, 52), true));
            Grid.SetColumn(cell, i % 2); Grid.SetRow(cell, i / 2 + (heading ? 1 : 0)); grid.Children.Add(cell);
        }
        grid.Opacity = appearance.ContentOpacity / 100d; Child = grid;
    }
}
