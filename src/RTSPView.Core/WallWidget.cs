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
    public int Margin { get; init; } = 12;
    public WeatherOptions? Weather { get; init; }
    public AircraftOptions? Aircraft { get; init; }
    public (double Width, double Height, double Left, double Top) Bounds(double width, double height)
    {
        var margin = Math.Min(Margin, Math.Min(width, height) / 2);
        var availableWidth = Math.Max(0, width - margin * 2); var availableHeight = Math.Max(0, height - margin * 2);
        var w = availableWidth * WidthPercent / 100;
        var natural = Kind == "aircraft"
            ? AircraftGeometry.Bounds(new AircraftOverlay { Aircraft = Aircraft!, WidthPercent = 100, Margin = 0 }, w, availableHeight).Height
            : WeatherGeometry.Bounds(new WeatherOverlay { Weather = Weather!, WidthPercent = 100, Margin = 0 }, w, availableHeight).Height;
        var h = HeightPercent is { } percent ? availableHeight * percent / 100 : natural;
        return (w, h, margin + (availableWidth - w) * X / 100, margin + (availableHeight - h) * Y / 100);
    }
    public void Validate(WallLayout layout)
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64 || (!double.IsFinite(WidthPercent) || WidthPercent is < 1 or > 100) || (!double.IsFinite(X) || X is < 0 or > 100) || (!double.IsFinite(Y) || Y is < 0 or > 100) || (HeightPercent is { } h && (!double.IsFinite(h) || h is <= 0 or > 100)) || Margin is < 0 or > 80 ||
            (HostCameraSlot != 0 && !layout.Tiles.Any(t => t.Kind == "camera" && t.CameraSlot == HostCameraSlot)))
            throw new InvalidDataException("Widget placement must fit its layout and reference an existing tile or the whole layout.");
        if (Kind == "weather" && Weather is not null && Aircraft is null) Weather.Validate();
        else if (Kind == "aircraft" && Aircraft is not null && Weather is null) Aircraft.Validate();
        else throw new InvalidDataException("Choose weather or aircraft settings for each widget.");
    }
}
