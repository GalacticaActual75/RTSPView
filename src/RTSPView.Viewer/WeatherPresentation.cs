using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Viewer;

public partial class MainWindow
{
    private readonly Dictionary<string, WeatherView> _weatherTiles = [];
    private readonly Dictionary<int, (Canvas Host, WeatherView View)> _weatherOverlays = [];
    private WeatherSnapshot[] _weatherSnapshots = [];
    private DispatcherTimer? _weatherTimer;
    private bool _readingWeather;
    private void SyncWeather()
    {
        if (!_settings.Plugins.Weather)
        {
            _weatherTimer?.Stop(); _weatherTimer = null;
            foreach (var view in _weatherTiles.Values) WallGrid.Children.Remove(view);
            _weatherTiles.Clear();
            foreach (var entry in _weatherOverlays.Values) (entry.Host.Parent as System.Windows.Controls.Panel)?.Children.Remove(entry.Host);
            _weatherOverlays.Clear(); _weatherSnapshots = [];
            return;
        }
        if (_weatherTimer is null)
        {
            _weatherTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _weatherTimer.Tick += async (_, _) => await ReadWeatherAsync(); _weatherTimer.Start();
            _ = ReadWeatherAsync();
        }
        var placements = EffectiveFocusedSlot.HasValue ? [] : EffectiveLayout.Tiles.Where(t => t.Kind == "weather").ToArray();
        foreach (var id in _weatherTiles.Keys.Where(id => !placements.Any(t => t.ItemId == id)).ToArray()) { WallGrid.Children.Remove(_weatherTiles[id]); _weatherTiles.Remove(id); }
        foreach (var tile in placements)
        {
            if (!_weatherTiles.TryGetValue(tile.ItemId, out var view)) { view = new WeatherView(); _weatherTiles[tile.ItemId] = view; WallGrid.Children.Add(view); }
            Grid.SetRow(view, tile.Row); Grid.SetColumn(view, tile.Column); Grid.SetRowSpan(view, tile.RowSpan); Grid.SetColumnSpan(view, tile.ColumnSpan);
            view.Update(tile.Weather!, _weatherSnapshots.FirstOrDefault(s => s.Key == tile.Weather!.CacheKey));
        }
        foreach (var camera in _tiles)
        {
            var setting = _settings.WeatherOverlays.FirstOrDefault(o => o.Enabled && o.HostCameraSlot == camera.Slot);
            if (setting is null)
            {
                if (_weatherOverlays.Remove(camera.Slot, out var previous)) camera.ContentOverlayRoot.Children.Remove(previous.Host);
                continue;
            }
            if (!_weatherOverlays.TryGetValue(camera.Slot, out var entry))
            {
                var host = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
                var view = new WeatherView(); host.Children.Add(view); camera.ContentOverlayRoot.Children.Insert(0, host);
                entry = (host, view); _weatherOverlays[camera.Slot] = entry;
                host.SizeChanged += (_, _) => PositionWeather(camera.Slot);
            }
            entry.View.Update(setting.Weather, _weatherSnapshots.FirstOrDefault(s => s.Key == setting.Weather.CacheKey));
            PositionWeather(camera.Slot);
        }
    }
    private void PositionWeather(int slot)
    {
        if (!_weatherOverlays.TryGetValue(slot, out var entry)) return;
        var o = _settings.WeatherOverlays.FirstOrDefault(o => o.Enabled && o.HostCameraSlot == slot); if (o is null) return;
        var bounds = WeatherGeometry.Bounds(o, entry.Host.ActualWidth, entry.Host.ActualHeight);
        entry.View.Width = bounds.Width;
        entry.View.Height = bounds.Height;
        Canvas.SetLeft(entry.View, bounds.Left);
        Canvas.SetTop(entry.View, bounds.Top);
    }
    private async Task ReadWeatherAsync()
    {
        if (_readingWeather || _weatherTimer is null) return;
        _readingWeather = true;
        try
        {
            var snapshots = await WeatherCache.ReadAsync(AppPaths.DataDirectory);
            // A transient file read failure must not discard usable cached readings.
            if (snapshots.Length > 0) _weatherSnapshots = snapshots;
            if (_weatherTimer is not null) SyncWeather();
        }
        finally { _readingWeather = false; }
    }
}
