namespace RTSPView.Core;

// The presentation contract used by both widget types. Keep browser parity covered by tests.
public sealed record WidgetViewport(double Width, double Height, double Padding, double Font, double Heading, double Reading, int DetailLevel, bool Wide)
{
    public static WidgetViewport For(double width, double height, string density = "auto")
    {
        width = Math.Max(1, width); height = Math.Max(1, height);
        var padding = Math.Clamp(Math.Min(width, height) * .045, 4, 24);
        var font = Math.Clamp(Math.Min(width / 22, height / 13), 12, 32);
        var level = width < 160 || height < 150 ? 0 : width < 420 || height < 260 ? 1 : 2;
        if (density == "minimal") level = 0;
        if (density == "standard") level = Math.Min(level, 1);
        if (density == "detailed" && width >= 160 && height >= 150) level = 2;
        return new(width, height, padding, font, font * 1.35,
            Math.Clamp(Math.Min(width * .22, height * .28), 24, 160), level, width > height * 2.2);
    }
    public static WidgetViewport WeatherFor(double width, double height, string density = "auto")
    {
        var v = For(width, height, density);
        if (width < 600 || height < 260 || width < height * 1.3) return v with { Wide = false, Font = Math.Clamp(Math.Min(width / 16, height / 11), 12, 48), Heading = Math.Clamp(Math.Min(width / 16, height / 11), 12, 48) * 1.35, Reading = Math.Clamp(Math.Min(width * .32, height * .4), 24, 240) };
        var font = Math.Clamp(Math.Min(width / 22, height / 10), 12, 80);
        return v with { Wide = true, Font = font, Heading = font * 1.35,
            Reading = Math.Clamp(Math.Min(width * .19, height * .46), 24, 360) };
    }
    public static void ValidateDensity(string value)
    {
        if (value is not ("auto" or "minimal" or "standard" or "detailed"))
            throw new InvalidDataException("Choose Auto, Minimal, Standard or Detailed information density.");
    }
}

// Photo space is independent of optional text rows and information density.
public sealed record AircraftPhotoViewport(bool Visible, bool Columns, double TextWidth, double TextHeight, double PhotoWidth, double PhotoHeight)
{
    public static AircraftPhotoViewport For(double width, double height, bool emphasis = false, bool paired = false)
    {
        var columns = !paired && width >= 280 && height >= 90;
        var visible = columns || width >= 160 && height >= 220;
        var fraction = emphasis ? .5 : .44;
        var photoWidth = columns ? (width - 8) * fraction : width;
        var photoHeight = columns ? height : (height - 8) * fraction;
        return new(visible, columns, columns ? width - 8 - photoWidth : width,
            columns ? height : height - 8 - photoHeight, photoWidth, photoHeight);
    }
}
