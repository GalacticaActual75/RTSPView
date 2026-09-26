using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RTSPView.Core;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Orientation = System.Windows.Controls.Orientation;

namespace RTSPView.Viewer;

public sealed class AircraftView : Border
{
    private AircraftOptions _options = new();
    private AircraftSnapshot? _snapshot;
    private AircraftRotation _rotation = new();

    private bool _overlay;
    private string? _lastUpdate;
    public AircraftView() { ClipToBounds = true; IsHitTestVisible = false; SizeChanged += (_, _) => Render(); }
    public void Update(AircraftOptions options, AircraftSnapshot? snapshot, bool overlay = false)
    {
        if (_options.CacheKey != options.CacheKey) _rotation = new();
        _options = options; _snapshot = snapshot; _overlay = overlay;
        var now = DateTimeOffset.UtcNow;
        var update = System.Text.Json.JsonSerializer.Serialize(new { options, snapshot, overlay,
            Freshness = snapshot?.Freshness(now), Matching = AircraftSelection.Nearby(options, snapshot, now).Select(a => a.Hex), Rotation = _rotation.Select(AircraftSelection.Nearby(options, snapshot, now), options.Preset == "board" ? options.MaximumAircraft : 1, now).Select(a => a.Hex) });
        if (update == _lastUpdate) return;
        _lastUpdate = update; Render();
    }
    private void Render()
    {
        var o = _options; var now = DateTimeOffset.UtcNow;
        var nearby = AircraftSelection.Nearby(o, _snapshot, now);
        var freshness = _snapshot?.Freshness(now) ?? "unavailable";
        Visibility = _overlay && o.HideWhenEmpty && (freshness != "fresh" || nearby.Length == 0) ? Visibility.Hidden : Visibility.Visible;
        WidgetAppearance.Apply(this, o.Appearance);
        var foreground = o.Theme == "light" ? Brushes.Black : Brushes.White;
        var muted = new SolidColorBrush(o.Theme == "light" ? Color.FromRgb(70, 80, 95) : Color.FromRgb(160, 174, 190));
        var accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(o.Accent));
        var width = Math.Max(0, ActualWidth - o.Padding * 2);
        var height = Math.Max(0, ActualHeight - o.Padding * 2 - 18);
        var size = Math.Min(o.FontSize, Math.Max(12, width / 5));
        var content = new Grid(); content.RowDefinitions.Add(new()); content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true }; content.Children.Add(stack);
        var credit = new TextBlock { Text = (freshness == "stale" ? "Outdated · " : freshness == "unavailable" ? "Unavailable · " : "") + "ADSB.lol · ODbL 1.0", FontSize = 10, Foreground = muted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 3, 0, 0) };
        credit.Text += " · adsbdb";
        Grid.SetRow(credit, 1); content.Children.Add(credit);
        TextBlock Text(string text, double font, bool secondary = false) => new() { Text = text, FontFamily = new("Segoe UI"), FontSize = font,
            Foreground = secondary ? muted : foreground, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = text, TextAlignment = o.Alignment switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left }, Margin = new(0, 1, 0, 1) };
        stack.Children.Add(Text(o.Location + " · Nearby aircraft", Math.Max(12, size * .6), true));
        var flights = new System.Windows.Controls.Primitives.UniformGrid { Columns = 1, Tag = "flights" };
        var groups = new List<StackPanel>();
        if (freshness == "unavailable")
        {
            stack.Children.Add(Text("Aircraft data unavailable", Math.Max(12, size * .65)));
            if (!string.IsNullOrWhiteSpace(_snapshot?.LastError))
            {
                var error = Text(_snapshot.LastError, Math.Max(12, size * .5), true); error.TextWrapping = TextWrapping.Wrap; stack.Children.Add(error);
            }
        }
        else if (nearby.Length == 0) stack.Children.Add(Text(freshness == "stale" ? "Waiting for fresh positions" : "No aircraft nearby", Math.Max(12, size * .65)));
        else
        {
            var display = _rotation.Select(nearby, o.Preset == "board" ? o.MaximumAircraft : 1, now);
            flights.Columns = display.Length > 1 ? 2 : 1;
            stack.Children.Add(flights);
            var columnWidth = Math.Max(0, width / flights.Columns - (flights.Columns > 1 ? 8 : 0));
            foreach (var a in display)
            {
                var group = new StackPanel { Margin = new(0, 4, flights.Columns > 1 ? 8 : 0, 4) };
                var heading = new Grid(); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); heading.ColumnDefinitions.Add(new());
                heading.Children.Add(new TextBlock { Text = "✈", FontFamily = new("Segoe UI Symbol"), FontSize = Math.Min(o.IconSize, Math.Max(14, width * .2)), Foreground = accent, Margin = new(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                var label = Text(string.IsNullOrWhiteSpace(a.RegisteredOwner) ? a.Label : a.RegisteredOwner, size * (o.Preset == "board" ? 1 : 1.5)); label.FontWeight = FontWeights.SemiBold; Grid.SetColumn(label, 1); heading.Children.Add(label); group.Children.Add(heading);
                var body = new Grid { Tag = "body" }; body.ColumnDefinitions.Add(new()); body.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var data = new StackPanel { Tag = "data" }; body.Children.Add(data); group.Children.Add(body);
                var hasPhoto = o.ShowPhoto && a.Photo is { IsValid: true } && columnWidth >= 220;
                var dataWidth = hasPhoto ? (columnWidth - 10) / 2 : columnWidth;
                var identity = new System.Windows.Controls.Primitives.UniformGrid { Columns = 1 };
                if (o.Fields.Contains("type")) identity.Children.Add(Text(string.IsNullOrWhiteSpace(a.Type) ? "Aircraft type unavailable" : a.Type, Math.Max(12, size * .6), true));
                if (!string.IsNullOrWhiteSpace(a.Registration)) identity.Children.Add(Text(a.Registration, Math.Max(12, size * .6), true));
                data.Children.Add(identity);
                if (!string.IsNullOrWhiteSpace(a.Callsign) && a.Callsign != a.Registration && !string.IsNullOrWhiteSpace(a.RegisteredOwner)) { var callsign = Text(a.Callsign, Math.Max(12, size * .6), true); callsign.Tag = "optional"; data.Children.Add(callsign); }
                var details = new System.Windows.Controls.Primitives.UniformGrid { Columns = dataWidth >= 300 ? 2 : 1, Tag = "optional" };
                foreach (var field in o.Fields.Where(f => f is "airline" or "destination")) details.Children.Add(Text(AircraftSelection.Metric(field, a, o), Math.Max(12, size * .6), true));
                if (details.Children.Count > 0) data.Children.Add(details);
                var metrics = new System.Windows.Controls.Primitives.UniformGrid { Columns = dataWidth >= 400 ? 3 : dataWidth >= 180 ? 2 : 1 };
                foreach (var field in o.Fields.Where(f => f is not ("type" or "owner" or "airline" or "destination"))) metrics.Children.Add(Text(AircraftSelection.Metric(field, a, o), Math.Max(12, size * .6), true));
                if (metrics.Children.Count > 0) { metrics.Tag = "optional"; data.Children.Add(metrics); }
                flights.Children.Add(group); groups.Add(group);
                // Keep the photo and its credit together; omit both when the tile is too small.
                if (hasPhoto && a.Photo is { } photo)
                {
                    var photoWidth = (columnWidth - 10) / 2;
                    var picture = new StackPanel { Tag = "photo", Width = photoWidth, Margin = new(10, 0, 0, 0) };
                    var image = new System.Windows.Controls.Image { Width = photoWidth, Height = Math.Min(120, photoWidth * 2 / 3), Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
                    if (photo.Representative) picture.Children.Add(Text("Representative photo", 10, true));
                    picture.Children.Add(image);
                    picture.Children.Add(Text(photo.Credit, 10, true));

                    Grid.SetColumn(picture, 1); body.Children.Add(picture);
                    _ = ShowPhotoAsync(image, picture, photo);
                }
            }
        }
        var omitted = false;
        if (groups.Count > 0)
        {
            var title = (FrameworkElement)stack.Children[0]; title.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            var rows = (int)Math.Ceiling(groups.Count / (double)flights.Columns);
            var groupHeight = Math.Max(0, (height - title.DesiredSize.Height) / rows - 8);
            var groupWidth = Math.Max(0, width / flights.Columns - (flights.Columns > 1 ? 8 : 0));
            foreach (var group in groups)
            {
                var data = (StackPanel)((Grid)group.Children[1]).Children[0];
                group.Measure(new System.Windows.Size(groupWidth, double.PositiveInfinity));
                while (group.DesiredSize.Height > groupHeight && data.Children.OfType<FrameworkElement>().LastOrDefault(n => Equals(n.Tag, "optional")) is { } detail)
                {
                    data.Children.Remove(detail); data.InvalidateMeasure(); group.InvalidateMeasure();
                    group.Measure(new System.Windows.Size(groupWidth, double.PositiveInfinity)); omitted = true;
                }
                group.MaxHeight = groupHeight; group.ClipToBounds = true;
            }
        }
        if (nearby.Length > groups.Count) credit.Text = $"+{nearby.Length - groups.Count} nearby · " + credit.Text;
        if (omitted) credit.Text = "Details hidden · " + credit.Text;
        // Replace the completed visual tree once, retaining the previous card while it is built.
        Child = content;
        System.Windows.Automation.AutomationProperties.SetHelpText(this, omitted ? "Enlarge the aircraft tile or widget to show more details." : "");
    }
    private static async Task ShowPhotoAsync(System.Windows.Controls.Image image, StackPanel container, AircraftPhoto photo)
    {
        var bitmap = await AircraftPhotoImages.Get(photo);
        if (bitmap is null) { container.Visibility = Visibility.Collapsed; }
        else image.Source = bitmap;
    }
}
