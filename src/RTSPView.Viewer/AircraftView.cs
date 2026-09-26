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
    private bool _takeover;
    private bool? _shown;
    private int _fadeGeneration;
    private string? _lastUpdate;
    public AircraftView() { ClipToBounds = true; IsHitTestVisible = false; SizeChanged += (_, _) => Render(); }
    public void Update(AircraftOptions options, AircraftSnapshot? snapshot, bool overlay = false, bool takeover = false)
    {
        if (_options.CacheKey != options.CacheKey) _rotation = new();
        _options = options; _snapshot = snapshot; _overlay = overlay; _takeover = takeover;
        var now = DateTimeOffset.UtcNow;
        var update = System.Text.Json.JsonSerializer.Serialize(new { options, snapshot, overlay, takeover,
            Freshness = snapshot?.Freshness(now), Matching = AircraftSelection.Nearby(options, snapshot, now).Select(a => a.Hex), Rotation = _rotation.Select(AircraftSelection.Nearby(options, snapshot, now), options.CardDesign == "board" ? 2 : options.Preset == "board" ? options.MaximumAircraft : 1, now).Select(a => a.Hex) });
        if (update == _lastUpdate) return;
        _lastUpdate = update; Render();
    }
    private void Render()
    {
        var o = _options; var now = DateTimeOffset.UtcNow;
        var nearby = AircraftSelection.Nearby(o, _snapshot, now);
        var freshness = _snapshot?.Freshness(now) ?? "unavailable";
        var show = !((_takeover || _overlay && o.HideWhenEmpty) && (freshness != "fresh" || nearby.Length == 0));
        SetVisible(show);
        if (!show) return;
        WidgetAppearance.Apply(this, o.Appearance);
        var foreground = o.Theme == "light" ? Brushes.Black : Brushes.White;
        var muted = new SolidColorBrush(o.Theme == "light" ? Color.FromRgb(70, 80, 95) : Color.FromRgb(160, 174, 190));
        var accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(o.Accent));
        var width = Math.Max(0, ActualWidth - o.Padding * 2);
        var height = Math.Max(0, ActualHeight - o.Padding * 2);
        var size = Math.Min(o.FontSize, Math.Max(12, width / 5));
        var content = new Grid(); content.RowDefinitions.Add(new()); content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true }; content.Children.Add(stack);
        var credit = new TextBlock { Text = (freshness == "stale" ? "Outdated · " : freshness == "unavailable" ? "Unavailable · " : "") + "ADSB.lol · ODbL 1.0", FontSize = o.TextSize("footer"), Foreground = muted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 3, 0, 0) };
        credit.Text += " · adsbdb";
        Grid.SetRow(credit, 1); content.Children.Add(credit);
        TextBlock Text(string text, double font, bool secondary = false) => new() { Text = text, FontFamily = new("Segoe UI"), FontSize = font,
            Foreground = secondary ? muted : foreground, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = text, TextAlignment = o.Alignment switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left }, Margin = new(0, 1, 0, 1) };
        stack.Children.Add(Text(o.Location + " · Nearby aircraft", o.TextSize("location"), true));
        var flights = new System.Windows.Controls.Primitives.UniformGrid { Columns = 1, Tag = "flights" };
        var groups = new List<StackPanel>();
        if (freshness == "unavailable")
        {
            stack.Children.Add(Text("Aircraft data unavailable", o.TextSize("status")));
            if (!string.IsNullOrWhiteSpace(_snapshot?.LastError))
            {
                var error = Text(_snapshot.LastError, o.TextSize("status"), true); error.TextWrapping = TextWrapping.Wrap; stack.Children.Add(error);
            }
        }
        else if (nearby.Length == 0) stack.Children.Add(Text(freshness == "stale" ? "Waiting for fresh positions" : "No aircraft nearby", o.TextSize("status")));
        else
        {
            var display = _rotation.Select(nearby, o.CardDesign == "board" ? 2 : o.Preset == "board" ? o.MaximumAircraft : 1, now);
            flights.Columns = display.Length > 1 ? 2 : 1;
            stack.Children.Add(flights);
            var columnWidth = Math.Max(0, width / flights.Columns - (flights.Columns > 1 ? 8 : 0));
            foreach (var a in display)
            {
                var group = new StackPanel { Margin = new(0, 4, flights.Columns > 1 ? 8 : 0, 4) };
                var heading = new Grid(); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); heading.ColumnDefinitions.Add(new());
                heading.Children.Add(new TextBlock { Text = "✈", FontFamily = new("Segoe UI Symbol"), FontSize = Math.Min(o.IconSize, Math.Max(14, width * .2)), Foreground = accent, Margin = new(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                var label = Text(string.IsNullOrWhiteSpace(a.RegisteredOwner) ? a.Label : a.RegisteredOwner, o.TextSize("owner")); label.FontWeight = FontWeights.SemiBold; label.TextWrapping = TextWrapping.Wrap; label.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; label.LineHeight = label.FontSize * 1.25; label.MaxHeight = label.LineHeight * 2; Grid.SetColumn(label, 1); heading.Children.Add(label); group.Children.Add(heading);
                var body = new Grid { Tag = "body" }; body.ColumnDefinitions.Add(new()); body.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var data = new StackPanel { Tag = "data" }; body.Children.Add(data); group.Children.Add(body);
                var hasPhoto = o.ShowPhoto && columnWidth >= 220;
                var dataWidth = hasPhoto ? (columnWidth - 10) / 2 : columnWidth;
                var identity = new System.Windows.Controls.Primitives.UniformGrid { Columns = 1 };
                if (o.Fields.Contains("type")) { var model = Text(string.IsNullOrWhiteSpace(a.ModelName) ? "Aircraft type unavailable" : a.ModelName, o.TextSize("type"), true); model.TextWrapping = TextWrapping.Wrap; identity.Children.Add(model); }
                if (!string.IsNullOrWhiteSpace(a.Registration)) identity.Children.Add(Text(a.Registration, o.TextSize("registration"), true));
                data.Children.Add(identity);
                if (!string.IsNullOrWhiteSpace(a.Callsign) && a.Callsign != a.Registration && !string.IsNullOrWhiteSpace(a.RegisteredOwner)) { var callsign = Text(a.Callsign, o.TextSize("callsign"), true); callsign.Tag = "optional"; data.Children.Add(callsign); }
                var details = new System.Windows.Controls.Primitives.UniformGrid { Columns = dataWidth >= 300 ? 2 : 1, Tag = "optional" };
                foreach (var field in o.Fields.Where(f => (f is "airline" or "destination") && AircraftSelection.HasDetailValue(f == "airline" ? a.Airline : a.Destination))) details.Children.Add(Text(AircraftSelection.Metric(field, a, o), o.TextSize(field), true));
                if (details.Children.Count > 0) data.Children.Add(details);
                var metrics = new System.Windows.Controls.Primitives.UniformGrid { Columns = columnWidth >= 400 ? 3 : columnWidth >= 180 ? 2 : 1 };
                foreach (var field in o.Fields.Where(f => f is not ("type" or "owner" or "airline" or "destination"))) metrics.Children.Add(Text(AircraftSelection.Metric(field, a, o), o.TextSize(field), true));
                if (metrics.Children.Count > 0) { metrics.Tag = "optional"; group.Children.Add(metrics); }
                flights.Children.Add(group); groups.Add(group);
                // Keep the photo and its credit together; omit both when the tile is too small.
                if (hasPhoto)
                {
                    var photo = a.Photo is { IsValid: true } ? a.Photo : null;
                    var photoWidth = o.CardDesign is "photo" or "board" ? columnWidth : o.CardDesign == "data" ? Math.Min(84, columnWidth / 3) : (columnWidth - 10) / 2;
                    var picture = new StackPanel { Tag = "photo", Width = photoWidth, Margin = new(10, 0, 0, 0) };
                    var image = new System.Windows.Controls.Image { Width = photoWidth, Height = Math.Min(120, photoWidth * 2 / 3), Stretch = Stretch.UniformToFill, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
                    picture.Children.Add(image);
                    var photoCredit = Text(photo is null ? "" : (photo.Representative ? "Representative photo · " : "") + photo.Credit, o.TextSize("photoCredit"), true);
                    photoCredit.Height = o.TextSize("photoCredit") * 2.5; picture.Children.Add(photoCredit);

                    image.SizeChanged += (_, _) => image.Clip = new RectangleGeometry(new Rect(0, 0, image.ActualWidth, image.ActualHeight), 6, 6);
                    Grid.SetColumn(picture, 1); body.Children.Add(picture);
                    if (o.CardDesign is "photo" or "board")
                    {
                        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = GridLength.Auto });
                        Grid.SetColumnSpan(data, 2); Grid.SetColumnSpan(picture, 2); Grid.SetColumn(picture, 0);
                        Grid.SetRow(picture, o.CardDesign == "photo" ? 0 : 1); Grid.SetRow(data, o.CardDesign == "photo" ? 1 : 0); picture.Margin = new(0,4,0,4);
                    }
                    else if (o.CardDesign == "data")
                    {
                        body.ColumnDefinitions[0].Width = GridLength.Auto; body.ColumnDefinitions[1].Width = new GridLength(1,GridUnitType.Star);
                        Grid.SetColumn(picture, 0); Grid.SetColumn(data, 1); picture.Margin = new(0,0,10,0);
                    }
                    if (photo is not null) _ = ShowPhotoAsync(image, picture, photo);
                }
            }
        }
        var omitted = false;
        if (groups.Count > 0)
        {
            var title = (FrameworkElement)stack.Children[0]; title.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            var rows = (int)Math.Ceiling(groups.Count / (double)flights.Columns);
            credit.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            var groupHeight = Math.Max(0, (height - title.DesiredSize.Height - credit.DesiredSize.Height) / rows);
            var groupWidth = Math.Max(0, width / flights.Columns - (flights.Columns > 1 ? 8 : 0));
            foreach (var group in groups)
            {
                var body = (Grid)group.Children[1];
                var data = (StackPanel)body.Children[0];
                var heading = (FrameworkElement)group.Children[0]; heading.Measure(new System.Windows.Size(groupWidth, double.PositiveInfinity));
                if (body.Children.OfType<StackPanel>().FirstOrDefault(n => Equals(n.Tag, "photo")) is { } picture)
                {
                    var image = picture.Children.OfType<System.Windows.Controls.Image>().Single();
                    var creditHeight = 0d;
                    foreach (var text in picture.Children.OfType<TextBlock>()) { text.Measure(new System.Windows.Size(picture.Width, double.PositiveInfinity)); creditHeight += text.DesiredSize.Height; }
                    var reserved = 0d;
                    foreach (var row in group.Children.OfType<FrameworkElement>().Skip(2)) { row.Measure(new System.Windows.Size(groupWidth, double.PositiveInfinity)); reserved += row.DesiredSize.Height; }
                    if (o.CardDesign is "photo" or "board") { data.Measure(new System.Windows.Size(groupWidth, double.PositiveInfinity)); reserved += data.DesiredSize.Height + 8; }
                    var available = Math.Max(0, groupHeight - group.Margin.Top - group.Margin.Bottom - heading.DesiredSize.Height - creditHeight - reserved);
                    if (available < 48) picture.Visibility = Visibility.Collapsed;
                    else image.Height = Math.Min(image.Height, available);
                    if (o.CardDesign is "compact" or "data")
                    {
                        data.Measure(new System.Windows.Size(Math.Max(1, groupWidth - picture.Width - 10), double.PositiveInfinity));
                        if (data.DesiredSize.Height > groupHeight - group.Margin.Top - group.Margin.Bottom - heading.DesiredSize.Height - reserved + 1)
                        { picture.Visibility = Visibility.Collapsed; Grid.SetColumn(data, 0); Grid.SetColumnSpan(data, 2); }
                    }
                }
                group.Measure(new System.Windows.Size(groupWidth, double.PositiveInfinity));
                while (group.DesiredSize.Height > groupHeight + 1 && data.Children.OfType<FrameworkElement>().Concat(group.Children.OfType<FrameworkElement>()).LastOrDefault(n => Equals(n.Tag, "optional")) is { } detail)
                {
                    if (data.Children.Contains(detail)) data.Children.Remove(detail); else group.Children.Remove(detail); data.InvalidateMeasure(); group.InvalidateMeasure();
                    group.Measure(new System.Windows.Size(groupWidth, double.PositiveInfinity)); omitted = true;
                }
                group.MaxHeight = groupHeight; group.ClipToBounds = true;
            }
        }
        if (o.CardDesign == "photo") foreach (var group in groups)
        {
            var body = (Grid)group.Children[1];
            if (body.Children.OfType<StackPanel>().FirstOrDefault(n => Equals(n.Tag, "photo")) is { } picture)
            { body.Children.Remove(picture); group.Children.Insert(0, picture); }
        }
        if (nearby.Length > groups.Count) credit.Text = $"+{nearby.Length - groups.Count} nearby · " + credit.Text;
        if (omitted) credit.Text = "Details hidden · " + credit.Text;
        // Replace the completed visual tree once, retaining the previous card while it is built.
        Child = content;
        System.Windows.Automation.AutomationProperties.SetHelpText(this, omitted ? "Enlarge the aircraft tile or widget to show more details." : "");
    }
    private void SetVisible(bool show)
    {
        var hidden = _takeover ? Visibility.Collapsed : Visibility.Hidden;
        if (!_options.FadeEnabled)
        {
            _shown = show; _fadeGeneration++; BeginAnimation(OpacityProperty, null); Opacity = show ? 1 : 0; Visibility = show ? Visibility.Visible : hidden; return;
        }
        if (_shown == show) return;
        var first = _shown is null; _shown = show; var generation = ++_fadeGeneration;
        var from = first ? 0 : Opacity;
        BeginAnimation(OpacityProperty, null);
        if (first && !show) { Opacity = 0; Visibility = hidden; return; }
        Visibility = Visibility.Visible; Opacity = show ? 1 : 0;
        var milliseconds = show ? _options.FadeInMilliseconds : _options.FadeOutMilliseconds;
        if (milliseconds == 0) { Visibility = show ? Visibility.Visible : hidden; return; }
        var animation = new System.Windows.Media.Animation.DoubleAnimation(from, show ? 1 : 0, TimeSpan.FromMilliseconds(milliseconds));
        animation.Completed += (_, _) => { if (generation != _fadeGeneration) return; BeginAnimation(OpacityProperty, null); Opacity = show ? 1 : 0; Visibility = show ? Visibility.Visible : hidden; };
        BeginAnimation(OpacityProperty, animation);
    }
    private static async Task ShowPhotoAsync(System.Windows.Controls.Image image, StackPanel container, AircraftPhoto photo)
    {
        while (true)
        {
            var bitmap = await AircraftPhotoImages.Get(photo);
            if (bitmap is not null) { image.Source = bitmap; return; }
            // Retry even when the traffic snapshot is unchanged; never move the text on failure.
            await Task.Delay(TimeSpan.FromSeconds(65));
            if (!image.IsLoaded) return;
        }
    }
}
