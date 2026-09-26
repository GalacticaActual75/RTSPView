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
        foreach (var widget in widgets)
        {
            var id = EffectiveLayout.Id + "/" + widget.Kind + "/" + widget.Id;
            if (!_layoutWidgets.TryGetValue(id, out var view)) { view = widget.Kind == "weather" ? new WeatherView() : new AircraftView(); _layoutWidgets[id] = view; LayoutWidgetLayer.Children.Add(view); }
            var scale = LayoutWidgetLayer.ActualWidth / EffectiveLayout.EffectiveWidth;
            var bounds = widget.Bounds(EffectiveLayout.EffectiveWidth, EffectiveLayout.EffectiveHeight);
            var contentScale = widget.ContentScaleFor(bounds.Width,bounds.Height);
            view.Width = bounds.Width / contentScale; view.Height = bounds.Height / contentScale;
            view.LayoutTransform = new System.Windows.Media.ScaleTransform(scale * contentScale,scale * contentScale);
            Canvas.SetLeft(view, bounds.Left * scale); Canvas.SetTop(view, bounds.Top * scale);
            if (view is WeatherView weather) weather.Update(widget.Weather!, _weatherSnapshots.FirstOrDefault(s => s.Key == widget.Weather!.CacheKey));
            else if (view is AircraftView aircraft) aircraft.Update(widget.Aircraft!, _aircraftSnapshots.FirstOrDefault(s => s.Key == widget.Aircraft!.CacheKey), overlay: true);
        }
    }
}
