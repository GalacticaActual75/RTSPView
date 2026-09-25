using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RTSPView.Core;
using Color = System.Windows.Media.Color;

namespace RTSPView.Viewer;

// The weather and aircraft cards intentionally share the same visual surface.
public static class WidgetAppearance
{
    public static void Apply(Border view, WeatherOptions options)
    {
        var bg = options.Theme == "light" ? Color.FromRgb(240, 244, 249) : Color.FromRgb(18, 22, 29);
        bg.A = (byte)(options.BackgroundOpacity * 255 / 100);
        view.Background = new SolidColorBrush(bg);
        view.CornerRadius = new(options.CornerRadius);
        view.Padding = new(options.Padding);
    }
}
