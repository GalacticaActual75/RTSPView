using System.Windows;
using System.Windows.Controls;
using RTSPView.Core;

namespace RTSPView.Viewer;

public partial class MainWindow
{
    private readonly Dictionary<string, FrameworkElement> _layoutWidgets = [];
    private void LayoutWidgets_SizeChanged(object sender, SizeChangedEventArgs e) => SyncLayoutWidgets();
    private void SyncLayoutWidgets()
    {
        if (_settings is null || LayoutWidgetLayer is null) return;
        var widgets = EffectiveFocusedSlot.HasValue ? [] : EffectiveLayout.Widgets.Where(w => w.Enabled && (w.Kind == "weather" ? _settings.Plugins.Weather : _settings.Plugins.Aircraft)).ToArray();
        var keys = widgets.Select(w => EffectiveLayout.Id + "/" + w.Kind + "/" + w.Id).ToHashSet();
        foreach (var id in _layoutWidgets.Keys.Where(id => !keys.Contains(id)).ToArray()) { LayoutWidgetLayer.Children.Remove(_layoutWidgets[id]); _layoutWidgets.Remove(id); }
        var tracks = WallProportions.Calculate(EffectiveLayout);
        foreach (var widget in widgets)
        {
            var id = EffectiveLayout.Id + "/" + widget.Kind + "/" + widget.Id;
            if (!_layoutWidgets.TryGetValue(id, out var view)) { view = widget.Kind == "weather" ? new WeatherView() : new AircraftView(); _layoutWidgets[id] = view; LayoutWidgetLayer.Children.Add(view); }
            double left = 0, top = 0, width = LayoutWidgetLayer.ActualWidth, height = LayoutWidgetLayer.ActualHeight;
            if (widget.HostCameraSlot != 0)
            {
                var tile = EffectiveLayout.Tiles.FirstOrDefault(t => t.Kind == "camera" && t.CameraSlot == widget.HostCameraSlot);
                if (tile is null) { view.Visibility = Visibility.Collapsed; continue; }
                left = tracks.Columns.Take(tile.Column).Sum() * width; top = tracks.Rows.Take(tile.Row).Sum() * height;
                width *= tracks.Columns.Skip(tile.Column).Take(tile.ColumnSpan).Sum(); height *= tracks.Rows.Skip(tile.Row).Take(tile.RowSpan).Sum();
            }
            var bounds = widget.Bounds(width, height); view.Width = bounds.Width; view.Height = bounds.Height;
            Canvas.SetLeft(view, left + bounds.Left); Canvas.SetTop(view, top + bounds.Top);
            if (view is WeatherView weather) weather.Update(widget.Weather!, _weatherSnapshots.FirstOrDefault(s => s.Key == widget.Weather!.CacheKey));
            else if (view is AircraftView aircraft) aircraft.Update(widget.Aircraft!, _aircraftSnapshots.FirstOrDefault(s => s.Key == widget.Aircraft!.CacheKey), overlay: true);
        }
    }
}
