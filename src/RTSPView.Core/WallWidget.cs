namespace RTSPView.Core;

public sealed record WallWidget
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "weather";
    public int HostCameraSlot { get; init; }
    public bool Enabled { get; init; } = true;
    public double WidthPercent { get; init; } = 40;
    public double X { get; init; } = 50;
    public double Y { get; init; } = 50;
    public double? HeightPercent { get; init; }
    public double ContentScale { get; init; } = 1;
    public int Margin { get; init; } = 12;
    public WeatherOptions? Weather { get; init; }
    public AircraftOptions? Aircraft { get; init; }
    public SystemStatsOptions? SystemStats { get; init; }
    public DateTimeOptions? DateTime { get; init; }
    public WidgetCell? Cell { get; init; }
    public (double Width, double Height, double Left, double Top) Bounds(WallLayout layout)
    {
        if (Cell is not { } cell) return Bounds(layout.EffectiveWidth, layout.EffectiveHeight);
        var grid = WallProportions.Calculate(layout);
        // A resized grid retains the widget and clamps its anchor/span into the new grid.
        var row = Math.Min(cell.Row, layout.Rows - 1); var column = Math.Min(cell.Column, layout.Columns - 1);
        return (grid.Columns.Skip(column).Take(cell.ColumnSpan).Sum() * layout.EffectiveWidth,
            grid.Rows.Skip(row).Take(cell.RowSpan).Sum() * layout.EffectiveHeight,
            grid.Columns.Take(column).Sum() * layout.EffectiveWidth, grid.Rows.Take(row).Sum() * layout.EffectiveHeight);
    }
    public (double Width, double Height, double Left, double Top) Bounds(double width, double height)
    {
        var margin = Math.Min(Margin, Math.Min(width, height) / 2);
        var availableWidth = Math.Max(0, width - margin * 2); var availableHeight = Math.Max(0, height - margin * 2);
        var w = availableWidth * WidthPercent / 100;
        var natural = Kind == "aircraft"
            ? AircraftGeometry.Bounds(new AircraftOverlay { Aircraft = Aircraft!, WidthPercent = 100, Margin = 0 }, w, availableHeight).Height
            : Kind == "weather" ? WeatherGeometry.Bounds(new WeatherOverlay { Weather = Weather!, WidthPercent = 100, Margin = 0 }, w, availableHeight).Height : Math.Min(availableHeight, w * .5625);
        var h = HeightPercent is { } percent ? availableHeight * percent / 100 : natural;
        return (w, h, margin + (availableWidth - w) * X / 100, margin + (availableHeight - h) * Y / 100);
    }
    public double ContentScaleFor(double width, double height)
    {
        return 1; // Layout at tile dimensions; never shrink a fixed reference canvas.
    }
    public void Validate(WallLayout layout)
    {
        if (Cell is { } cell && (cell.Row is < 0 or > 11 || cell.Column is < 0 or > 11 || cell.RowSpan is < 1 or > 12 || cell.ColumnSpan is < 1 or > 12))
            throw new InvalidDataException("Choose a valid widget grid position and span.");
        if (!double.IsFinite(ContentScale) || ContentScale is < .05 or > 20 || string.IsNullOrWhiteSpace(Id) || Id.Length > 64 || (!double.IsFinite(WidthPercent) || WidthPercent is < 1 or > 100) || (!double.IsFinite(X) || X is < 0 or > 100) || (!double.IsFinite(Y) || Y is < 0 or > 100) || (HeightPercent is { } h && (!double.IsFinite(h) || h is <= 0 or > 100)) || Margin is < 0 or > 80 ||
            (HostCameraSlot != 0 && !layout.Tiles.Any(t => t.Kind == "camera" && t.CameraSlot == HostCameraSlot)))
            throw new InvalidDataException("Widget placement must fit its layout and reference an existing tile or the whole layout.");
        if (new object?[] { Weather, Aircraft, SystemStats, DateTime }.Count(o => o is not null) != 1) throw new InvalidDataException("Choose exactly one widget's settings.");
        if (Kind == "weather" && Weather is not null) Weather.Validate();
        else if (Kind == "aircraft" && Aircraft is not null) Aircraft.Validate();
        else if (Kind == "systemStats" && SystemStats is not null) SystemStats.Validate();
        else if (Kind == "dateTime" && DateTime is not null) DateTime.Validate();
        else throw new InvalidDataException("Choose valid settings for this widget.");
    }
}

public sealed record WidgetCell
{
    public int Row { get; init; }
    public int Column { get; init; }
    public int RowSpan { get; init; } = 1;
    public int ColumnSpan { get; init; } = 1;
}
