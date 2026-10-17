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
    private AircraftPhotoGate _photoGate = new();

    private bool _overlay;
    private bool _takeover;
    private bool? _shown;
    private int _fadeGeneration;
    private string? _contentKey;
    private bool _changingContent;
    private string? _lastUpdate;
    public AircraftView() { ClipToBounds = true; IsHitTestVisible = false; SizeChanged += (_, _) => Render(); }
    public void Update(AircraftOptions options, AircraftSnapshot? snapshot, bool overlay = false, bool takeover = false)
    {
        if (_options.CacheKey != options.CacheKey) _rotation = new();
        if (_options.CacheKey != options.CacheKey || _options.WaitForPhoto != options.WaitForPhoto || _options.ShowPhoto != options.ShowPhoto || _options.PhotoWaitSeconds != options.PhotoWaitSeconds) _photoGate = new();
        _options = options; _snapshot = snapshot; _overlay = overlay; _takeover = takeover;
        var now = DateTimeOffset.UtcNow;
        var update = System.Text.Json.JsonSerializer.Serialize(new { options, snapshot, overlay, takeover,
            RotationTick = now.ToUnixTimeSeconds() / 20, RetryMinute = now.ToUnixTimeSeconds() / 65, PhotoTick = options.WaitForPhoto && options.ShowPhoto ? now.ToUnixTimeSeconds() : 0, Freshness = snapshot?.Freshness(now), Matching = AircraftSelection.Nearby(options, snapshot, now).Select(a => a.Hex) });
        if (update == _lastUpdate) return;
        _lastUpdate = update; Render();
    }
    private void Render()
    {
        var o = _options; var now = DateTimeOffset.UtcNow;
        var nearby = AircraftSelection.Nearby(o, _snapshot, now);
        var waiting = false;
        _photoGate.Retain(nearby.Select(a => a.Hex));
        if (o.WaitForPhoto && o.ShowPhoto)
        {
            var readyAircraft = new List<AircraftTrack>();
            foreach (var aircraft in nearby)
            {
                var task = aircraft.Photo is { IsValid: true } photo ? AircraftPhotoImages.Get(photo) : null;
                if (task is { IsCompleted: false } && _pendingPhotos.Add(task)) _ = PhotoReady(task);
                var ready = task is { IsCompletedSuccessfully: true, Result: not null };
                var failed = task is { IsCompleted: true } && !ready;
                var decision = _photoGate.Decide(aircraft.Hex, ready, failed, now, o.PhotoWaitSeconds);
                if (decision == AircraftPhotoDecision.Waiting) waiting = true;
                else readyAircraft.Add(decision == AircraftPhotoDecision.WithoutPhoto ? aircraft with { Photo = null } : aircraft);
            }
            nearby = readyAircraft.ToArray();
        }
        var freshness = _snapshot?.Freshness(now) ?? "unavailable";
        var show = !((_takeover || _overlay && o.HideWhenEmpty) && (freshness != "fresh" || nearby.Length == 0)) && !((_overlay || _takeover) && waiting && nearby.Length == 0);
        var count = (o.CardDesign == "board" || o.Preset == "board") && ActualWidth >= 600 && ActualHeight >= 240 ? 2 : 1;
        var selected = _rotation.Select(nearby, count, now);
        var contentKey = selected.Length == 0 ? freshness + ":" + waiting : string.Join("|", selected.Select(a =>
            a.Hex + ":" + (o.ShowPhoto && a.Photo is { IsValid: true } photo && AircraftPhotoImages.Get(photo) is { IsCompletedSuccessfully: true, Result: not null } ? photo.Url : "")));
        if (!show || !o.FadeEnabled)
        {
            _changingContent = false;
            _contentKey = null;
        }
        if (show && o.FadeEnabled && _shown == true && _contentKey != null && _contentKey != contentKey)
        {
            if (!_changingContent)
            {
                _changingContent = true;
                var generation = ++_fadeGeneration;
                var animation = new System.Windows.Media.Animation.DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(o.FadeOutMilliseconds));
                animation.Completed += (_, _) =>
                {
                    if (generation != _fadeGeneration) return;
                    BeginAnimation(OpacityProperty, null); Opacity = 0;
                    _changingContent = false; _contentKey = null; _shown = false;
                    Render(); // Render the latest snapshot only after the old content has faded away.
                };
                BeginAnimation(OpacityProperty, animation);
            }
            return;
        }
        if (_changingContent) return;
        SetVisible(show);
        if (!show) return;
        _contentKey = contentKey;
        var viewport = WidgetViewport.For(ActualWidth, ActualHeight, o.Density);
        var appearance = o.Appearance with { Padding = (int)viewport.Padding };
        WidgetAppearance.Apply(this, appearance);
        var flow = new WidgetFlow { Opacity = o.ContentOpacity / 100d }; Child = flow;
        void Line(string text, double size, int priority) => flow.Add(WidgetFlow.Text(text, size, appearance), priority);
        if (o.ShowHeading && viewport.DetailLevel > 0) Line(o.Location + " · Nearby aircraft", viewport.Font, 35);
        if (freshness == "unavailable")
        {
            Line(_snapshot is null ? "Waiting for aircraft" : "Aircraft data unavailable", viewport.Heading, 100);
            if (!string.IsNullOrWhiteSpace(_snapshot?.LastError)) Line(_snapshot.LastError, viewport.Font, 20);
        }
        else if (nearby.Length == 0) Line(waiting ? "Waiting for aircraft photo" : freshness == "stale" ? "Waiting for fresh positions" : "No aircraft nearby", viewport.Heading, 100);
        else
        {
            var board = new System.Windows.Controls.Primitives.UniformGrid { Columns = selected.Length };
            flow.Add(board, 100);
            var photoRows = new List<(Grid Flight, WidgetFlow Text, double Width, double Budget, double Height, System.Windows.Controls.Image Image, TextBlock Credit)>();
            foreach (var aircraft in selected)
            {
                var reserved = (o.ShowHeading && viewport.DetailLevel > 0 ? viewport.Font * 1.25 + 3 : 0) + 10 * 1.25 + 6;
                var column = new WidgetFlow { AlignTop = selected.Length > 1, MaxHeight = Math.Max(1, ActualHeight - viewport.Padding * 2 - reserved), Margin = new Thickness(0) };
                var width = Math.Max(1, (ActualWidth - viewport.Padding * 2 - (selected.Length > 1 ? 8 : 0)) / selected.Length);
                var font = selected.Length > 1 ? Math.Clamp(Math.Min(width / 16, column.MaxHeight / 13), 12, 28) : viewport.Font;
                var flight = new Grid { Margin = new Thickness(selected.Length > 1 && board.Children.Count > 0 ? 4 : 0, 0, selected.Length > 1 && board.Children.Count == 0 ? 4 : 0, 0) }; board.Children.Add(flight); flight.Children.Add(column);
                if (selected.Length > 1) flight.Height = column.MaxHeight;
                void Detail(string value, double size, int priority) => column.Add(WidgetFlow.Text(value, size, appearance), priority);
                var identity = WidgetFlow.Text(aircraft.Label, Math.Min(font * 1.35, width / (Math.Max(1, aircraft.Label.Length) * .75)), appearance);
                identity.TextWrapping = TextWrapping.NoWrap; identity.TextTrimming = TextTrimming.CharacterEllipsis;
                identity.FontWeight = FontWeights.SemiBold;
                identity.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(o.Accent)); column.Add(identity, 100);
                var order = o.OrderedFields();
                foreach (var field in order)
                {
                    if (field != "registration" && !o.Fields.Contains(field)) continue;
                    if (viewport.DetailLevel == 0 && field is not ("altitude" or "distance")) continue;
                    if (viewport.DetailLevel < 2 && field is "airline" or "destination") continue;
                    var value = field switch
                    {
                        "type" => string.IsNullOrWhiteSpace(aircraft.ModelName) ? aircraft.Type : aircraft.ModelName,
                        "registration" => aircraft.Registration != aircraft.Label ? aircraft.Registration : "",
                        "owner" => aircraft.RegisteredOwner,
                        "airline" => AircraftSelection.HasDetailValue(aircraft.Airline) ? AircraftSelection.Metric(field, aircraft, o) : "",
                        "destination" => AircraftSelection.HasDetailValue(aircraft.Destination) ? AircraftSelection.Metric(field, aircraft, o) : "",
                        _ => AircraftSelection.Metric(field, aircraft, o)
                    };
                    if (AircraftSelection.HasDetailValue(value)) Detail(value, font, 90 - Array.IndexOf(order, field));
                }
                if (o.ShowPhoto && aircraft.Photo is { IsValid: true } photo)
                {
                    var task = AircraftPhotoImages.Get(photo);
                    if (task.IsCompletedSuccessfully && task.Result is {} bitmap)
                    {
                        var height = column.MaxHeight;
                        var layout = AircraftPhotoViewport.For(width, height, o.CardDesign == "photo", selected.Length > 1);
                        if (layout.Visible)
                        {
                            var credit = WidgetFlow.Text((photo.Representative ? "Representative · " : "") + photo.Credit, 10, appearance);
                            credit.Measure(new System.Windows.Size(layout.PhotoWidth, double.PositiveInfinity));
                            var imageHeight = layout.PhotoHeight - credit.DesiredSize.Height - 3;
                            if (imageHeight >= 32)
                            {
                                var picture = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                                var ratio = bitmap.PixelWidth / (double)Math.Max(1, bitmap.PixelHeight);
                                var photoWidth = Math.Min(layout.PhotoWidth, imageHeight * ratio);
                                var image = new System.Windows.Controls.Image { Source = bitmap, Width = photoWidth, Height = photoWidth / ratio,
                                    Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                                var radius = Math.Clamp(Math.Min(image.Width, image.Height) * .12, 10, 28);
                                image.Clip = new RectangleGeometry(new Rect(0, 0, image.Width, image.Height), radius, radius);
                                picture.Children.Add(image);
                                picture.Children.Add(credit);
                                flight.Height = height;
                                column.Margin = new Thickness(0);
                                column.MaxHeight = layout.TextHeight;
                                if (layout.Columns)
                                {
                                    flight.ColumnDefinitions.Add(new() { Width = new GridLength(layout.TextWidth) });
                                    flight.ColumnDefinitions.Add(new() { Width = new GridLength(8) });
                                    flight.ColumnDefinitions.Add(new() { Width = new GridLength(layout.PhotoWidth) });
                                    Grid.SetColumn(picture, 2);
                                }
                                else
                                {
                                    flight.RowDefinitions.Add(new() { Height = new GridLength(layout.TextHeight) });
                                    flight.RowDefinitions.Add(new() { Height = new GridLength(8) });
                                    flight.RowDefinitions.Add(new() { Height = new GridLength(layout.PhotoHeight) });
                                    Grid.SetRow(picture, 2);
                                    if (selected.Length > 1)
                                    {
                                        picture.VerticalAlignment = VerticalAlignment.Top;
                                        picture.Children.Remove(credit);
                                        flight.RowDefinitions.Add(new() { Height = new GridLength(3) });
                                        flight.RowDefinitions.Add(new() { Height = GridLength.Auto });
                                        Grid.SetRow(credit, 4); flight.Children.Add(credit);
                                        photoRows.Add((flight, column, layout.TextWidth, layout.TextHeight, height, image, credit));
                                    }
                                }
                                flight.Children.Add(picture);
                            }
                        }
                    }
                    else if (!task.IsCompleted && _pendingPhotos.Add(task)) _ = PhotoReady(task);
                }
            }
            // Share the actual visible text height, rather than leaving unused text space above photos.
            if (photoRows.Count > 0)
            {
                foreach (var row in photoRows) row.Text.Measure(new System.Windows.Size(row.Width, row.Budget));
                var textHeight = photoRows.Max(row => row.Text.DesiredSize.Height);
                var creditHeight = photoRows.Max(row => row.Credit.DesiredSize.Height);
                foreach (var row in photoRows)
                {
                    var imageHeight = Math.Max(1, row.Height - textHeight - 8 - 3 - creditHeight);
                    row.Flight.RowDefinitions[0].Height = new GridLength(textHeight);
                    row.Flight.RowDefinitions[2].Height = new GridLength(imageHeight);
                    row.Flight.RowDefinitions[4].Height = new GridLength(creditHeight);
                    row.Credit.VerticalAlignment = VerticalAlignment.Bottom;
                    var ratio = row.Image.Width / row.Image.Height;
                    row.Image.Width = Math.Min(row.Width, imageHeight * ratio);
                    row.Image.Height = row.Image.Width / ratio;
                    var radius = Math.Clamp(Math.Min(row.Image.Width, row.Image.Height) * .12, 10, 28);
                    row.Image.Clip = new RectangleGeometry(new Rect(0, 0, row.Image.Width, row.Image.Height), radius, radius);
                }
            }
        }
        Line((freshness == "stale" ? "Outdated · " : "") + "ADSB.lol · ODbL · adsbdb" + (nearby.Any(a => a.DetailsSource == "FAA") ? " · FAA" : ""), 10, 95);
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
