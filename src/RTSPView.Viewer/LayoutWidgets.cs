using System.Windows;
using System.Windows.Controls;
using RTSPView.Core;

namespace RTSPView.Viewer;

public partial class MainWindow
{
    private Window? _layoutWidgetWindow;
    private void PlaceLayoutWidgetWindow()
    {
        if (!IsLoaded || !CanDisplayOverlayWindows() || _layoutWidgets.Count == 0 || WallGrid.ActualWidth <= 0 || WallGrid.ActualHeight <= 0)
        {
            _layoutWidgetWindow?.Hide();
            return;
        }
        if (_layoutWidgetWindow is null)
        {
            // VideoView owns native windows. A sibling WPF element cannot draw over them.
            ((System.Windows.Controls.Panel)LayoutWidgetLayer.Parent).Children.Remove(LayoutWidgetLayer);
            LayoutWidgetLayer.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = WallGrid });
            LayoutWidgetLayer.SetBinding(HeightProperty, new System.Windows.Data.Binding("ActualHeight") { Source = WallGrid });
            _layoutWidgetWindow = new Window
            {
                Owner = this, Title = "RTSPView layout widgets", WindowStyle = WindowStyle.None,
                AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
                Focusable = false, IsHitTestVisible = false, Content = LayoutWidgetLayer
            };
            _layoutWidgetWindow.SourceInitialized += (_, _) =>
            {
                ConfigureOverlayWindow(_layoutWidgetWindow);
                var handle = new System.Windows.Interop.WindowInteropHelper(_layoutWidgetWindow).Handle;
                // Make the entire overlay click-through, including opaque card backgrounds.
                SetWindowLongPtr(handle, GwlExStyle, new IntPtr(GetWindowLongPtr(handle, GwlExStyle).ToInt64() | 0x20));
            };
        }
        var origin = WallGrid.PointToScreen(new System.Windows.Point());
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(WallGrid);
        _layoutWidgetWindow.Left = origin.X / dpi.DpiScaleX;
        _layoutWidgetWindow.Top = origin.Y / dpi.DpiScaleY;
        _layoutWidgetWindow.Width = WallGrid.ActualWidth;
        _layoutWidgetWindow.Height = WallGrid.ActualHeight;
        if (!_layoutWidgetWindow.IsVisible) _layoutWidgetWindow.Show();
        BringOverlayWindowToFront(_layoutWidgetWindow);
    }
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
            if (!_layoutWidgets.TryGetValue(id, out var view)) { view = widget.Kind == "weather" ? new WeatherView { IndependentWidget = true } : new AircraftView(); _layoutWidgets[id] = view; LayoutWidgetLayer.Children.Add(view); }
            var scale = LayoutWidgetLayer.ActualWidth / EffectiveLayout.EffectiveWidth;
            var bounds = widget.Bounds(EffectiveLayout);
            var contentScale = 1d;
            view.Width = bounds.Width / contentScale; view.Height = bounds.Height / contentScale;
            view.LayoutTransform = new System.Windows.Media.ScaleTransform(scale * contentScale,scale * contentScale);
            Canvas.SetLeft(view, bounds.Left * scale); Canvas.SetTop(view, bounds.Top * scale);
            if (view is WeatherView weather) weather.Update(widget.Weather!, _weatherSnapshots.FirstOrDefault(s => s.Key == widget.Weather!.CacheKey));
            else if (view is AircraftView aircraft) aircraft.Update(widget.Aircraft!, _aircraftSnapshots.FirstOrDefault(s => s.Key == widget.Aircraft!.CacheKey), overlay: true);
        }
        PlaceLayoutWidgetWindow();
    }
}
