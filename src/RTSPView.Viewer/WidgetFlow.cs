using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RTSPView.Core;

namespace RTSPView.Viewer;

// A bounded, priority-aware flow. Remove whole optional rows before sacrificing readability.
public sealed class WidgetFlow : System.Windows.Controls.Panel
{
    private readonly Dictionary<UIElement, int> _priority = [];
    private readonly List<UIElement> _visible = [];
    public double Gap { get; init; } = 3;
    public bool AlignTop { get; init; }
    public void Add(UIElement child, int priority) { Children.Add(child); _priority[child] = priority; }
    protected override System.Windows.Size MeasureOverride(System.Windows.Size available)
    {
        _visible.Clear();
        foreach (UIElement child in Children) { child.Measure(new(available.Width, double.PositiveInfinity)); _visible.Add(child); }
        double Height() => _visible.Sum(c => c.DesiredSize.Height) + Math.Max(0, _visible.Count - 1) * Gap;
        while (_visible.Count > 0 && (Height() > available.Height || _visible.Any(c => c.DesiredSize.Width > available.Width + .5)))
        {
            var next = _visible.OrderBy(c => _priority[c]).First(); _visible.Remove(next);
        }
        return new(double.IsInfinity(available.Width) ? _visible.Select(c => c.DesiredSize.Width).DefaultIfEmpty().Max() : available.Width,
            Math.Min(available.Height, Height()));
    }
    protected override System.Windows.Size ArrangeOverride(System.Windows.Size final)
    {
        var height = _visible.Sum(c => c.DesiredSize.Height) + Math.Max(0, _visible.Count - 1) * Gap;
        var y = AlignTop ? 0 : Math.Max(0, (final.Height - height) / 2);
        foreach (UIElement child in Children)
        {
            if (!_visible.Contains(child)) { child.Arrange(new Rect(0, 0, 0, 0)); child.Opacity = 0; continue; }
            child.Opacity = 1; child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height)); y += child.DesiredSize.Height + Gap;
        }
        return final;
    }
    public static TextBlock Text(string value, double size, WeatherOptions options, bool muted = false) => new()
    {
        Text = value, ToolTip = value, FontFamily = new("Segoe UI"), FontSize = size,
        Foreground = options.Theme == "light" ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White,
        TextWrapping = TextWrapping.Wrap, TextAlignment = options.Alignment switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left },
        LineHeight = size * 1.25, LineStackingStrategy = LineStackingStrategy.BlockLineHeight
    };
}
