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
            RetryMinute = now.ToUnixTimeSeconds() / 65, Freshness = snapshot?.Freshness(now), Matching = AircraftSelection.Nearby(options, snapshot, now).Select(a => a.Hex), Rotation = _rotation.Select(AircraftSelection.Nearby(options, snapshot, now), options.CardDesign == "board" ? 2 : options.Preset == "board" ? options.MaximumAircraft : 1, now).Select(a => a.Hex) });
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
        var viewport = WidgetViewport.For(ActualWidth, ActualHeight, o.Density);
        var appearance = o.Appearance with { Padding = (int)viewport.Padding };
        WidgetAppearance.Apply(this, appearance);
        var flow = new WidgetFlow(); Child = flow;
        void Line(string text, double size, int priority) => flow.Add(WidgetFlow.Text(text, size, appearance), priority);
        if (viewport.DetailLevel > 0) Line(o.Location + " · Nearby aircraft", viewport.Font, 35);
        if (freshness == "unavailable")
        {
            Line(_snapshot is null ? "Waiting for aircraft" : "Aircraft data unavailable", viewport.Heading, 100);
            if (!string.IsNullOrWhiteSpace(_snapshot?.LastError)) Line(_snapshot.LastError, viewport.Font, 20);
        }
        else if (nearby.Length == 0) Line(freshness == "stale" ? "Waiting for fresh positions" : "No aircraft nearby", viewport.Heading, 100);
        else
        {
            var count = (o.CardDesign == "board" || o.Preset == "board") && ActualWidth >= 600 && ActualHeight >= 240 ? 2 : 1;
            var selected = _rotation.Select(nearby, count, now);
            var board = new System.Windows.Controls.Primitives.UniformGrid { Columns = selected.Length };
            flow.Add(board, 100);
            foreach (var aircraft in selected)
            {
                var reserved = (viewport.DetailLevel > 0 ? viewport.Font * 1.25 + 3 : 0) + Math.Max(10, viewport.Font * .75) * 1.25 + 6;
                var column = new WidgetFlow { MaxHeight = Math.Max(1, ActualHeight - viewport.Padding * 2 - reserved), Margin = new Thickness(0, 0, selected.Length > 1 ? 8 : 0, 0) };
                board.Children.Add(column);
                void Detail(string value, double size, int priority) => column.Add(WidgetFlow.Text(value, size, appearance), priority);
                var identity = WidgetFlow.Text(aircraft.Label, viewport.Heading, appearance);
                identity.FontWeight = FontWeights.SemiBold;
                identity.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(o.Accent)); column.Add(identity, 100);
                foreach (var field in new[] { "altitude", "distance", "speed", "track", "verticalRate" })
                    if (o.Fields.Contains(field) && (viewport.DetailLevel > 0 || field is "altitude" or "distance")) Detail(AircraftSelection.Metric(field, aircraft, o), viewport.Font, field == "altitude" ? 95 : field == "distance" ? 90 : field == "speed" ? 80 : 25);
                if (viewport.DetailLevel > 0)
                {
                    if (o.Fields.Contains("type")) Detail(string.IsNullOrWhiteSpace(aircraft.ModelName) ? aircraft.Type : aircraft.ModelName, viewport.Font, 70);
                    if (aircraft.Registration != aircraft.Label && AircraftSelection.HasDetailValue(aircraft.Registration)) Detail(aircraft.Registration, viewport.Font, 65);
                    if (o.Fields.Contains("owner") && AircraftSelection.HasDetailValue(aircraft.RegisteredOwner)) Detail(aircraft.RegisteredOwner, viewport.Font, 40);
                }
                if (viewport.DetailLevel > 1)
                    foreach (var field in new[] { "airline", "destination" }) if (o.Fields.Contains(field) && AircraftSelection.HasDetailValue(field == "airline" ? aircraft.Airline : aircraft.Destination)) Detail(AircraftSelection.Metric(field, aircraft, o), viewport.Font, 20);
                if (o.ShowPhoto && viewport.DetailLevel > 0 && aircraft.Photo is { IsValid: true } photo)
                {
                    var task = AircraftPhotoImages.Get(photo);
                    if (task.IsCompletedSuccessfully && task.Result is {} bitmap)
                    {
                        var picture = new StackPanel();
                        picture.Children.Add(new System.Windows.Controls.Image { Source = bitmap, Height = Math.Clamp(ActualHeight * .22, 48, 260), Stretch = Stretch.Uniform });
                        picture.Children.Add(WidgetFlow.Text((photo.Representative ? "Representative · " : "") + photo.Credit, Math.Max(10, viewport.Font * .75), appearance));
                        column.Add(picture, o.CardDesign == "photo" ? 75 : 45);
                    }
                    else if (!task.IsCompleted && _pendingPhotos.Add(task)) _ = PhotoReady(task);
                }
            }
        }
        Line((freshness == "stale" ? "Outdated · " : "") + "ADSB.lol · ODbL · adsbdb", Math.Max(10, viewport.Font * .75), 95);
    }
    private readonly HashSet<Task<System.Windows.Media.Imaging.BitmapSource?>> _pendingPhotos = [];
    private async Task PhotoReady(Task<System.Windows.Media.Imaging.BitmapSource?> task)
    {
        try { await task; } catch (Exception) { /* A failed photo never blocks the data card. */ } finally { _pendingPhotos.Remove(task); }
        if (IsLoaded) Render();
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
}
