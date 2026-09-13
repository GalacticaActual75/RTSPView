using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RTSPView.Core;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace RTSPView.Viewer;

public sealed class TemperatureWarningWindow : Window
{
    private readonly TextBlock _text;
    private bool _warning;
    public TemperatureWarningWindow(Window owner)
    {
        Owner = owner; Title = "RTSPView temperature warning"; WindowStyle = WindowStyle.None;
        AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; IsHitTestVisible = false; Focusable = false;
        Width = 340; Height = 62;
        _text = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 111, 111)), FontSize = 15,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(235, 28, 18, 22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 69, 69)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7), Padding = new Thickness(12, 8, 12, 8), Child = _text };
    }

    public bool SetStatus(TemperatureStatus? status, DateTimeOffset now)
    {
        var lines = new List<string>();
        if (status?.CpuWarning(now) == true) lines.Add($"CPU temperature {status.CpuC:0.#} °C > {status.Settings.CpuMaxC:0.#} °C");
        if (status?.GpuWarning(now) == true) lines.Add($"GPU temperature {status.GpuC:0.#} °C > {status.Settings.GpuMaxC:0.#} °C");
        _text.Text = string.Join("\n", lines);
        var warning = lines.Count > 0;
        if (warning != _warning)
        {
            _text.BeginAnimation(OpacityProperty, null);
            // Slow flashing text; the smoked panel stays solid and legible. Respect reduced animation.
            if (warning && SystemParameters.ClientAreaAnimation)
                _text.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(750))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            _warning = warning;
        }
        if (!warning) Hide();
        return warning;
    }
}
