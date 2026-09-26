namespace RTSPView.Core;

// Preserve visual positions while retiring camera-bound weather/aircraft presentations.
public static class IndependentWidgets
{
    public static WallLayout Convert(WallLayout layout, IReadOnlyList<WeatherOverlay> weather, IReadOnlyList<AircraftOverlay> aircraft)
    {
        var grid = WallProportions.Calculate(layout);
        var width = layout.EffectiveWidth; var height = layout.EffectiveHeight;
        var widgets = new List<WallWidget>();
        (double Left, double Top, double Width, double Height) Rect(WallTile tile) =>
            (grid.Columns.Take(tile.Column).Sum() * width, grid.Rows.Take(tile.Row).Sum() * height,
             grid.Columns.Skip(tile.Column).Take(tile.ColumnSpan).Sum() * width, grid.Rows.Skip(tile.Row).Take(tile.RowSpan).Sum() * height);
        void Add(WallWidget widget, double left, double top, double w, double h)
        {
            var stem = widget.Id[..Math.Min(widget.Id.Length,56)]; var id = stem; for (var suffix = 1; widgets.Any(x => x.Id == id); suffix++) id = stem + "-" + suffix;
            widgets.Add(widget with { Id = id, HostCameraSlot = 0, Margin = 0, WidthPercent = Math.Clamp(w / width * 100,1,100), HeightPercent = Math.Clamp(h / height * 100,.01,100),
                X = width > w ? Math.Clamp(left / (width - w) * 100,0,100) : 0, Y = height > h ? Math.Clamp(top / (height - h) * 100,0,100) : 0 });
        }
        foreach (var widget in layout.Widgets)
        {
            var tile = layout.Tiles.FirstOrDefault(t => t.Kind == "camera" && t.CameraSlot == widget.HostCameraSlot);
            if (widget.HostCameraSlot == 0 || tile is null) { widgets.Add(widget with { HostCameraSlot = 0 }); continue; }
            var r = Rect(tile); var b = widget.Bounds(r.Width,r.Height); Add(widget,r.Left+b.Left,r.Top+b.Top,b.Width,b.Height);
        }
        foreach (var tile in layout.Tiles)
        {
            var r = Rect(tile);
            if (tile.Kind is "weather" or "aircraft" || tile.Aircraft is not null)
                Add(new WallWidget { Id = "content-" + (tile.ItemId.Length > 0 ? tile.ItemId : tile.CameraSlot.ToString()), Kind = tile.Kind == "weather" ? "weather" : "aircraft", Weather = tile.Weather,
                    Aircraft = tile.Aircraft is null ? null : tile.Aircraft with { HideWhenEmpty = tile.Kind == "camera" } }, r.Left,r.Top,r.Width,r.Height);
            foreach (var overlay in weather.Where(o => tile.Kind == "camera" && o.HostCameraSlot == tile.CameraSlot))
            { var b = WeatherGeometry.Bounds(overlay,r.Width,r.Height); Add(new WallWidget { Id = "weather-"+tile.CameraSlot, Kind = "weather", Enabled = overlay.Enabled, Weather = overlay.Weather },r.Left+b.Left,r.Top+b.Top,b.Width,b.Height); }
            foreach (var overlay in aircraft.Where(o => tile.Kind == "camera" && o.HostCameraSlot == tile.CameraSlot))
            { var b = AircraftGeometry.Bounds(overlay,r.Width,r.Height); Add(new WallWidget { Id = "aircraft-"+tile.CameraSlot, Kind = "aircraft", Enabled = overlay.Enabled, Aircraft = overlay.Aircraft },r.Left+b.Left,r.Top+b.Top,b.Width,b.Height); }
        }
        return layout with { Widgets = widgets, Tiles = layout.Tiles.Where(t => t.Kind == "camera").Select(t => t with { Aircraft = null }).ToArray(), RowWeights = grid.Rows, ColumnWeights = grid.Columns };
    }
}
