using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;
using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace RTSPView.Viewer;

// Lives beside WallViewport, outside every native video and shaped overlay window.
public sealed class WallDiagnosticsPanel : UserControl
{
    private readonly StackPanel _body = new() { Margin = new Thickness(12) };
    private readonly StackPanel _errors = new();
    private readonly ComboBox _camera = new() { Margin = new Thickness(0, 8, 0, 8), DisplayMemberPath = "Label", SelectedValuePath = "Slot" };
    private readonly TextBlock _stats = Text("");
    private readonly Button _toggle = new() { Content = "Diagnostics  ‹", MinHeight = 36 };
    private readonly Button _restart = new() { Content = "Restart selected stream", Margin = new Thickness(0, 12, 0, 8), MinHeight = 32 };
    private CameraTile[] _tiles = [];
    private string _cameraSignature = "";
    private string _errorSignature = "";
    private readonly Dictionary<int, TextBlock> _errorText = new();
    private HashSet<int> _warningSlots = [];
    private bool _fullScreen;
    private HashSet<int> _autoOpenExcluded = [];
    private bool _windowedOpen;
    public int? SelectedSlot => _camera.SelectedValue is int slot ? slot : null;
    public int WarningCount => _warningSlots.Count;
    private sealed record Choice(int Slot, string Label);

    public WallDiagnosticsPanel()
    {
        Width = 280;
        Background = new SolidColorBrush(Color.FromRgb(21, 23, 26));
        Foreground = Brushes.White;
        foreach (var button in new[] { _toggle, _restart })
        {
            button.Background = new SolidColorBrush(Color.FromRgb(48, 55, 65));
            button.Foreground = Brushes.White;
            button.BorderThickness = new Thickness(0);
        }
        var root = new DockPanel();
        DockPanel.SetDock(_toggle, Dock.Top); root.Children.Add(_toggle);
        root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _body.Children.Add(Text("Camera diagnostics", true));
        _body.Children.Add(Text("Select a feed, or hover over a camera."));
        _body.Children.Add(_camera); _body.Children.Add(_stats); _body.Children.Add(_restart);
        _body.Children.Add(Text("Connection alerts", true)); _body.Children.Add(_errors);
        Content = root;
        _toggle.Click += (_, _) => ToggleWindowed();
        _camera.SelectionChanged += (_, _) => RefreshStatistics();
        _restart.Click += (_, e) => { e.Handled = true; _tiles.FirstOrDefault(t => t.Slot == SelectedSlot)?.Start(); };
        ApplyVisibility();
    }

    private static TextBlock Text(string value, bool heading = false) => new()
    {
        Text = value, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White,
        FontSize = heading ? 14 : 12, FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
        Margin = new Thickness(0, 4, 0, 8)
    };

    public void SetFullScreen(bool fullScreen)
    {
        _fullScreen = fullScreen;
        ApplyVisibility();
    }

    public void ToggleWindowed()
    {
        if (_fullScreen) return;
        _windowedOpen = !_windowedOpen;
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        Visibility = (_fullScreen ? _warningSlots.Any(slot => !_autoOpenExcluded.Contains(slot)) : _windowedOpen) ? Visibility.Visible : Visibility.Collapsed;
        _toggle.Visibility = _fullScreen ? Visibility.Collapsed : Visibility.Visible;
        _toggle.Content = "Close diagnostics";
    }

    public void SelectCamera(int slot, bool open = false)
    {
        if (_tiles.Any(t => t.Slot == slot)) _camera.SelectedValue = slot;
        if (open && !_fullScreen) { _windowedOpen = true; ApplyVisibility(); }
    }

    public void Refresh(CameraTile[] tiles, IEnumerable<int>? autoOpenExcluded = null)
    {
        _autoOpenExcluded = (autoOpenExcluded ?? []).ToHashSet();
        _tiles = tiles.Where(t => t.DiagnosticsAvailable).ToArray();
        var choices = _tiles.Select(t => new Choice(t.Slot, t.DiagnosticLabel)).ToArray();
        var signature = string.Join("\n", choices.Select(c => c.ToString()));
        if (signature != _cameraSignature)
        {
            var selected = SelectedSlot;
            _cameraSignature = signature; _camera.ItemsSource = choices;
            _camera.SelectedValue = selected;
            if (_camera.SelectedIndex < 0 && choices.Length > 0) _camera.SelectedIndex = 0;
        }
        var warnings = _tiles.Select(t => (Tile: t, Message: t.DiagnosticWarning)).Where(w => w.Message is not null).ToArray();
        var nextSlots = warnings.Select(w => w.Tile.Slot).ToHashSet();
        _warningSlots = nextSlots;
        // Keep existing controls and scroll position stable while telemetry updates.
        var errorSignature = string.Join(",", warnings.Select(w => w.Tile.Slot));
        if (errorSignature != _errorSignature || _errors.Children.Count == 0)
        {
            _errorSignature = errorSignature; _errors.Children.Clear(); _errorText.Clear();
            foreach (var warning in warnings)
            {
                var slot = warning.Tile.Slot;
                var label = Text(""); _errorText[slot] = label;
                var button = new Button { Content = label,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Color.FromRgb(93, 43, 28)),
                    Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(8) };
                button.Click += (_, _) => SelectCamera(slot, true);
                _errors.Children.Add(button);
            }
            if (warnings.Length == 0) _errors.Children.Add(Text("No connection alerts."));
        }
        foreach (var warning in warnings) _errorText[warning.Tile.Slot].Text = warning.Tile.DiagnosticLabel + "\n" + warning.Message;
        ApplyVisibility();
        RefreshStatistics();
    }

    private void RefreshStatistics()
    {
        var tile = _tiles.FirstOrDefault(t => t.Slot == SelectedSlot);
        _restart.IsEnabled = tile is not null;
        if (tile is null) { _stats.Text = "No configured feeds."; return; }
        var t = tile.GetTelemetry();
        _stats.Text = $"{tile.DiagnosticLabel}\n{t.State}\n{t.Width ?? 0} × {t.Height ?? 0} • {t.Codec ?? "Unknown codec"}\n{t.Fps:0.0} fps • {t.BitrateKbps:0} kb/s\n{t.Decoder}\nReconnects: {t.ReconnectCount}\nUptime: {TimeSpan.FromSeconds(Math.Max(0, t.StreamUptimeSeconds ?? 0)):g}";
    }
}
