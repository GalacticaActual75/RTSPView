using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Viewer;

public partial class MainWindow
{
    private readonly Dictionary<string, AircraftView> _aircraftTiles = [];
    private readonly Dictionary<int, (Canvas Host, AircraftView View)> _aircraftOverlays = [];
    private readonly Dictionary<int, AircraftView> _aircraftReplacements = [];
    private AircraftSnapshot[] _aircraftSnapshots = [];
    private DispatcherTimer? _aircraftTimer;
    private bool _readingAircraft;
    private void SyncAircraft()
    {
        if (!_settings.Plugins.Aircraft)
        {
            _aircraftTimer?.Stop(); _aircraftTimer = null;
            foreach (var view in _aircraftTiles.Values) WallGrid.Children.Remove(view);
            _aircraftTiles.Clear();
            foreach (var entry in _aircraftOverlays.Values) (entry.Host.Parent as System.Windows.Controls.Panel)?.Children.Remove(entry.Host);
            _aircraftOverlays.Clear();
            foreach (var view in _aircraftReplacements.Values) (view.Parent as System.Windows.Controls.Panel)?.Children.Remove(view);
            _aircraftReplacements.Clear(); _aircraftSnapshots = [];
            return;
        }
        if (_aircraftTimer is null)
        {
            _aircraftTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _aircraftTimer.Tick += async (_, _) => await ReadAircraftAsync(); _aircraftTimer.Start();
            _ = ReadAircraftAsync();
        }
        var placements = EffectiveFocusedSlot.HasValue ? [] : EffectiveLayout.Tiles.Where(t => t.Kind == "aircraft").ToArray();
        foreach (var id in _aircraftTiles.Keys.Where(id => !placements.Any(t => t.ItemId == id)).ToArray()) { WallGrid.Children.Remove(_aircraftTiles[id]); _aircraftTiles.Remove(id); }
        foreach (var tile in placements)
        {
            if (!_aircraftTiles.TryGetValue(tile.ItemId, out var view)) { view = new AircraftView(); _aircraftTiles[tile.ItemId] = view; WallGrid.Children.Add(view); }
            Grid.SetRow(view, tile.Row); Grid.SetColumn(view, tile.Column); Grid.SetRowSpan(view, tile.RowSpan); Grid.SetColumnSpan(view, tile.ColumnSpan);
            view.Update(tile.Aircraft!, _aircraftSnapshots.FirstOrDefault(s => s.Key == tile.Aircraft!.CacheKey));
        }
        foreach (var camera in _tiles)
        {
            var replacement = EffectiveFocusedSlot.HasValue ? null : EffectiveLayout.Tiles.FirstOrDefault(t => t.Kind == "camera" && t.CameraSlot == camera.Slot)?.Aircraft;
            if (replacement is null)
            {
                if (_aircraftReplacements.Remove(camera.Slot, out var old)) camera.ContentOverlayRoot.Children.Remove(old);
            }
            else
            {
                if (!_aircraftReplacements.TryGetValue(camera.Slot, out var view))
                {
                    view = new AircraftView(); _aircraftReplacements[camera.Slot] = view;
                    System.Windows.Controls.Panel.SetZIndex(view, 10); camera.ContentOverlayRoot.Children.Add(view);
                }
                var snapshot = _aircraftSnapshots.FirstOrDefault(s => s.Key == replacement.CacheKey);
                view.Update(replacement, snapshot);
                view.Visibility = AircraftSelection.ShouldReplaceCamera(replacement, snapshot, DateTimeOffset.UtcNow) ? Visibility.Visible : Visibility.Collapsed;
            }
            var setting = _settings.AircraftOverlays.FirstOrDefault(o => o.Enabled && o.HostCameraSlot == camera.Slot);
            if (setting is null)
            {
                if (_aircraftOverlays.Remove(camera.Slot, out var previous)) camera.ContentOverlayRoot.Children.Remove(previous.Host);
                continue;
            }
            if (!_aircraftOverlays.TryGetValue(camera.Slot, out var entry))
            {
                var host = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
                var view = new AircraftView(); host.Children.Add(view); camera.ContentOverlayRoot.Children.Insert(0, host);
                entry = (host, view); _aircraftOverlays[camera.Slot] = entry;
                host.SizeChanged += (_, _) => PositionAircraft(camera.Slot);
            }
            entry.View.Update(setting.Aircraft, _aircraftSnapshots.FirstOrDefault(s => s.Key == setting.Aircraft.CacheKey), overlay: true);
            PositionAircraft(camera.Slot);
        }
    }
    private void PositionAircraft(int slot)
    {
        if (!_aircraftOverlays.TryGetValue(slot, out var entry)) return;
        var o = _settings.AircraftOverlays.FirstOrDefault(o => o.Enabled && o.HostCameraSlot == slot); if (o is null) return;
        var bounds = AircraftGeometry.Bounds(o, entry.Host.ActualWidth, entry.Host.ActualHeight);
        entry.View.Width = bounds.Width;
        entry.View.Height = bounds.Height;
        Canvas.SetLeft(entry.View, bounds.Left);
        Canvas.SetTop(entry.View, bounds.Top);
    }
    private async Task ReadAircraftAsync()
    {
        if (_readingAircraft || _aircraftTimer is null) return;
        _readingAircraft = true;
        try
        {
            var snapshots = await AircraftCache.ReadAsync(AppPaths.DataDirectory);
            // A transient file read failure must not discard usable cached readings.
            if (snapshots.Length > 0) _aircraftSnapshots = snapshots;
            if (_aircraftTimer is not null) SyncAircraft();
        }
        finally { _readingAircraft = false; }
    }
}
