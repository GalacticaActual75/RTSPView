using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RTSPView.Core;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace RTSPView.Viewer;

public sealed class TemperatureWarningWindow : Window
{
    private readonly TextBlock _text;
    private bool _warning;
    private readonly DispatcherTimer _flash = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly Border _border;
    private bool _bright = true;
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
        Content = _border = new Border { Background = new SolidColorBrush(Color.FromArgb(245, 28, 18, 22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 80, 80)), BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(20, 16, 20, 16), Child = panel };
        _flash.Tick += (_, _) => { _bright = !_bright; PaintFlash(); };
        Closed += (_, _) => _flash.Stop();
    }

    private void PaintFlash()
    {
        _border.Background = new SolidColorBrush(_bright ? Color.FromArgb(250, 115, 15, 20) : Color.FromArgb(245, 28, 18, 22));
        _text.Foreground = _bright ? Brushes.White : new SolidColorBrush(Color.FromRgb(255, 130, 130));
    }

    public void PlaceWithin(Point origin, Size viewport)
    {
        Width = Math.Min(520, Math.Max(1, viewport.Width - 32));
        var content = (FrameworkElement)Content;
        content.Measure(new Size(Width, double.PositiveInfinity));
        Left = origin.X + Math.Max(0, (viewport.Width - Width) / 2);
        Top = origin.Y + Math.Max(0, (viewport.Height - content.DesiredSize.Height) / 2);
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
            _flash.Stop(); _bright = true; PaintFlash();
            // An explicit alarm remains flashing even when Windows disables decorative animations.
            if (warning) _flash.Start();
            _warning = warning;
        }
        if (!warning) Hide();
        return warning;
    }
}
