namespace RTSPView.Core;

public sealed record WallWidget
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "weather";
    public int HostCameraSlot { get; init; }
    public bool Enabled { get; init; } = true;
    public int WidthPercent { get; init; } = 40;
    public int X { get; init; } = 50;
    public int Y { get; init; } = 50;
    public int Margin { get; init; } = 12;
    public WeatherOptions? Weather { get; init; }
    public AircraftOptions? Aircraft { get; init; }
    public (double Width, double Height, double Left, double Top) Bounds(double width, double height) => Kind == "aircraft"
        ? AircraftGeometry.Bounds(new AircraftOverlay { Aircraft = Aircraft!, WidthPercent = WidthPercent, X = X, Y = Y, Margin = Margin }, width, height)
        : WeatherGeometry.Bounds(new WeatherOverlay { Weather = Weather!, WidthPercent = WidthPercent, X = X, Y = Y, Margin = Margin }, width, height);
    public void Validate(WallLayout layout)
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64 || WidthPercent is < 15 or > 95 || X is < 0 or > 100 || Y is < 0 or > 100 || Margin is < 0 or > 80 ||
            (HostCameraSlot != 0 && !layout.Tiles.Any(t => t.Kind == "camera" && t.CameraSlot == HostCameraSlot)))
            throw new InvalidDataException("Widget placement must fit its layout and reference an existing tile or the whole layout.");
        if (Kind == "weather" && Weather is not null && Aircraft is null) Weather.Validate();
        else if (Kind == "aircraft" && Aircraft is not null && Weather is null) Aircraft.Validate();
        else throw new InvalidDataException("Choose weather or aircraft settings for each widget.");
    }
}
