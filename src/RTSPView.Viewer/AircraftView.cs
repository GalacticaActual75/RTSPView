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
    private string? _featured;
    private DateTimeOffset _selectedAt;
    private bool _overlay;
    public AircraftView() { ClipToBounds = true; IsHitTestVisible = false; SizeChanged += (_, _) => Render(); }
    public void Update(AircraftOptions options, AircraftSnapshot? snapshot, bool overlay = false)
    {
        if (_options.CacheKey != options.CacheKey) _featured = null;
        _options = options; _snapshot = snapshot; _overlay = overlay; Render();
    }
    private void Render()
    {
        var o = _options; var now = DateTimeOffset.UtcNow;
        var nearby = AircraftSelection.Nearby(o, _snapshot, now);
        var freshness = _snapshot?.Freshness(now) ?? "unavailable";
        Visibility = _overlay && o.HideWhenEmpty && freshness == "fresh" && nearby.Length == 0 ? Visibility.Hidden : Visibility.Visible;
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
        if (o.Fields.Any(f => f is "owner" or "airline" or "destination")) credit.Text += " · adsbdb";
        Grid.SetRow(credit, 1); content.Children.Add(credit); Child = content;
        TextBlock Text(string text, double font, bool secondary = false) => new() { Text = text, FontFamily = new("Segoe UI"), FontSize = font,
            Foreground = secondary ? muted : foreground, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = text, TextAlignment = o.Alignment switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left }, Margin = new(0, 1, 0, 1) };
        stack.Children.Add(Text(o.Location + " · Nearby aircraft", Math.Max(12, size * .6), true));
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
            var selected = nearby.FirstOrDefault(a => a.Hex == _featured);
            if (selected is null || now - _selectedAt >= TimeSpan.FromSeconds(20)) { selected = nearby[0]; _featured = selected.Hex; _selectedAt = now; }
            var display = o.Preset == "board" ? nearby.Take(o.MaximumAircraft) : [selected];
            foreach (var a in display)
            {
                var group = new StackPanel { Margin = new(0, 4, 0, 4) };
                var heading = new Grid(); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); heading.ColumnDefinitions.Add(new());
                heading.Children.Add(new TextBlock { Text = "✈", FontFamily = new("Segoe UI Symbol"), FontSize = Math.Min(o.IconSize, Math.Max(14, width * .2)), Foreground = accent, Margin = new(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                var label = Text(a.Label, size * (o.Preset == "board" ? 1 : 1.5)); label.FontWeight = FontWeights.SemiBold; Grid.SetColumn(label, 1); heading.Children.Add(label); group.Children.Add(heading);
                if (o.Fields.Contains("type")) { var type = AircraftSelection.Metric("type", a, o); if (type.Length > 0) group.Children.Add(Text(type, Math.Max(12, size * .6), true)); }
                foreach (var field in o.Fields.Where(f => f is "owner" or "airline" or "destination")) group.Children.Add(Text(AircraftSelection.Metric(field, a, o), Math.Max(12, size * .6), true));
                var metrics = new System.Windows.Controls.Primitives.UniformGrid { Columns = width >= 400 ? 3 : width >= 220 ? 2 : 1 };
                foreach (var field in o.Fields.Where(f => f is not ("type" or "owner" or "airline" or "destination"))) metrics.Children.Add(Text(AircraftSelection.Metric(field, a, o), Math.Max(12, size * .6), true));
                if (metrics.Children.Count > 0) group.Children.Add(metrics);
                stack.Children.Add(group);
                // Keep the photo and its credit together; omit both when the tile is too small.
                if (o.ShowPhoto && a.Photo is { IsValid: true } photo && width >= 180 && height >= 250)
                {
                    var picture = new StackPanel { Tag = "photo" };
                    var image = new System.Windows.Controls.Image { Height = Math.Min(o.Preset == "board" ? 80 : 180, height * .3), Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
                    picture.Children.Add(image);
                    picture.Children.Add(Text("Photo © " + photo.Photographer + " · Planespotters.net", 10, true));
                    group.Children.Insert(1, picture);
                    _ = ShowPhotoAsync(image, picture, photo);
                }
            }
            var count = o.Preset == "board" ? Math.Min(o.MaximumAircraft, nearby.Length) : 1;
            if (nearby.Length > count) credit.Text = $"+{nearby.Length - count} nearby · " + credit.Text;
        }
        stack.Measure(new System.Windows.Size(width, double.PositiveInfinity));
        var omitted = false;
        while (stack.DesiredSize.Height > height && stack.Children.Count > 1)
        {
            omitted = true;
            if (stack.Children.OfType<StackPanel>().SelectMany(g => g.Children.OfType<StackPanel>().Where(p => Equals(p.Tag, "photo")).Select(p => (Group: g, Photo: p))).FirstOrDefault() is var photoRow && photoRow.Photo is not null) photoRow.Group.Children.Remove(photoRow.Photo);
            else if (stack.Children.Count == 2 && stack.Children[1] is StackPanel group && group.Children.Count > 1) group.Children.RemoveAt(group.Children.Count - 1);
            else stack.Children.RemoveAt(stack.Children.Count - 1);
            // Removing a nested detail invalidates its group immediately, but WPF
            // propagates that invalidation to this stack on a later layout pass.
            // Remeasure now so fitting does not discard the entire flight using
            // the previous (oversized) measurement.
            stack.InvalidateMeasure();
            stack.Measure(new System.Windows.Size(width, double.PositiveInfinity));
        }
        if (omitted) credit.Text = "Details hidden · " + credit.Text;
        System.Windows.Automation.AutomationProperties.SetHelpText(this, omitted ? "Enlarge the aircraft tile or widget to show more details." : "");
    }
    private static async Task ShowPhotoAsync(System.Windows.Controls.Image image, StackPanel container, AircraftPhoto photo)
    {
        var bitmap = await AircraftPhotoImages.Get(photo);
        if (bitmap is null) container.Visibility = Visibility.Collapsed;
        else image.Source = bitmap;
    }
}
