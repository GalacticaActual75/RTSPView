using System.Windows;
using RTSPView.Core;

namespace RTSPView.Viewer;

public partial class MainWindow
{
    private readonly Dictionary<int, (CameraTile Tile, CameraSettings Camera)> _rawOverlaySources = new();

    private void SyncRawOverlaySources(WallLayout layout)
    {
        var sources = StreamCatalog.LayoutCameras(_settings).Where(c => StreamCatalog.IsOverlaySource(c.Slot)).ToArray();
        foreach (var slot in _rawOverlaySources.Keys.Where(slot => !sources.Any(c => c.Slot == slot)).ToArray())
        {
            var tile = _rawOverlaySources[slot].Tile;
            WallGrid.Children.Remove(tile); tile.Dispose(); _rawOverlaySources.Remove(slot);
        }
        foreach (var source in sources)
        {
            // Keep configured original sources warm across focus and layout changes.
            var camera = source with { Enabled = true };
            if (!_rawOverlaySources.TryGetValue(source.Slot, out var entry))
            {
                var tile = new CameraTile { SharedDiagnostics = true, Visibility = Visibility.Collapsed };
                tile.DiagnosticsRequested += Tile_DiagnosticsRequested;
                tile.PointerActivity += Tile_PointerActivity;
                tile.FocusRequested += Tile_FocusRequested;
                WallGrid.Children.Add(tile);
                // Share the untransformed decoded bitmap; keep independent mask/framing controls.
                var owner = new[] { DoorbellTile, GarageTile }.Concat(_additionalOverlays.Values.Select(e => e.Tile))
                    .Single(t => StreamCatalog.SourceSlot(t.Slot) == source.Slot);
                tile.Initialize(_libVlc, _logger, camera, _settings.RequestHardwareDecoding, compositedVideo: true, preserveWholeFrame: true, sharedSource: owner);
                tile.ApplyOverlayPreferences(_settings.ShowCameraNames, _settings.ShowCameraStats);
                entry = (tile, camera);
            }
            else if (entry.Camera != camera) entry.Tile.Apply(camera);
            entry.Tile.ApplyTileBorder(_settings.ShowTileBorders);
            _rawOverlaySources[source.Slot] = (entry.Tile, camera);
        }
        _allTiles = [.._tiles, DoorbellTile, GarageTile, .._additionalOverlays.Values.Select(e => e.Tile), .._rawOverlaySources.Values.Select(e => e.Tile)];
    }
}
