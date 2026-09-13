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
        Width = 520; SizeToContent = SizeToContent.Height;
        _text = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 130, 130)), FontSize = 26,
            FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var heading = new TextBlock { Text = "⚠  HIGH TEMPERATURE", FontSize = 18,
            FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(255, 111, 111)),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        var panel = new StackPanel();
        panel.Children.Add(heading); panel.Children.Add(_text);
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(245, 28, 18, 22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 80, 80)), BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(20, 16, 20, 16), Child = panel };
    }

    public bool SetStatus(TemperatureStatus? status, DateTimeOffset now)
    {
        var lines = new List<string>();
        if (status?.CpuWarning(now) == true) lines.Add($"CPU  {status.CpuC:0.#} °C  ·  Max {status.Settings.CpuMaxC:0.#} °C");
        if (status?.GpuWarning(now) == true) lines.Add($"GPU  {status.GpuC:0.#} °C  ·  Max {status.Settings.GpuMaxC:0.#} °C");
        _text.Text = string.Join("\n", lines);
        var warning = lines.Count > 0;
        if (warning != _warning)
        {
            _text.BeginAnimation(OpacityProperty, null);
            // Slow flashing text; the smoked panel stays solid and legible. Respect reduced animation.
            if (warning && SystemParameters.ClientAreaAnimation)
                _text.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.65, TimeSpan.FromMilliseconds(750))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            _warning = warning;
        }
        if (!warning) Hide();
        return warning;
    }
}
