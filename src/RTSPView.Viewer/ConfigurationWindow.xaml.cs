using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RTSPView.Core;
using RTSPView.Infrastructure;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace RTSPView.Viewer;

public partial class ConfigurationWindow : Window
{
    private readonly JsonSettingsStore _store;
    private AppSettings _settings;
    private readonly AppSettings _original;
    private bool _replacing;
    private int[] _indices = [];
    private int _currentSlot;
    private bool _loading;

    public AppSettings Settings => _settings;

    public ConfigurationWindow(AppSettings settings, JsonSettingsStore store)
    {
        InitializeComponent();
        _original = settings.Normalize();
        _settings = _original;
        _store = store;
        PopulateSlots();
        SlotBox.SelectedIndex = 0;
        LoadSlot(0);
    }

    private void SlotBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _loading) return;
        CommitSlot(_currentSlot);
        if (SlotBox.SelectedIndex >= 0) LoadSlot(SlotBox.SelectedIndex);
    }

    private void LoadSlot(int index)
    {
        if (_indices.Length == 0) { _loading = false; return; }
        _loading = true;
        _currentSlot = index;
        var camera = _settings.Cameras[_indices[index]];
        EnabledBox.IsChecked = camera.Enabled;
        NameBox.Text = camera.Name;
        UrlBox.Text = camera.RtspUrl;
        TransportBox.SelectedIndex = (int)camera.Transport;
        CacheBox.Text = camera.NetworkCacheMilliseconds.ToString();
        StartupBox.Text = camera.StartupTimeoutSeconds.ToString();
        WatchdogBox.Text = camera.WatchdogTimeoutSeconds.ToString();
        BackoffBox.Text = camera.MaximumReconnectBackoffSeconds.ToString();
        LowLatencyBox.IsChecked = camera.LowLatency;
        AudioBox.IsChecked = camera.DecodeAudio;
        CompositeBox.IsChecked = camera.CompositeStream;
        _loading = false;
    }

    private void CommitSlot(int index)
    {
        if (_indices.Length == 0) return;
        index = _indices[index];
        var cameras = _settings.Cameras.ToArray();
        var current = cameras[index];
        cameras[index] = current with
        {
            Enabled = EnabledBox.IsChecked == true,
            Name = NameBox.Text.Trim(),
            RtspUrl = UrlBox.Text.Trim(),
            Transport = (RtspTransport)Math.Max(0, TransportBox.SelectedIndex),
            NetworkCacheMilliseconds = Parse(CacheBox.Text, current.NetworkCacheMilliseconds),
            StartupTimeoutSeconds = Parse(StartupBox.Text, current.StartupTimeoutSeconds),
            WatchdogTimeoutSeconds = Parse(WatchdogBox.Text, current.WatchdogTimeoutSeconds),
            MaximumReconnectBackoffSeconds = Parse(BackoffBox.Text, current.MaximumReconnectBackoffSeconds),
            LowLatency = LowLatencyBox.IsChecked == true,
            CompositeStream = CompositeBox.IsChecked == true,
            DecodeAudio = AudioBox.IsChecked == true
        };
        _settings = (_settings with { Cameras = cameras }).Normalize();
    }

    private static int Parse(string value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitSlot(_currentSlot);
            _settings = await _store.SaveCameraEditsAsync(_original, _settings, _replacing);
            DialogResult = true;
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Configuration not saved", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "RTSPView configuration (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try { _loading = true; _settings = await _store.ImportAsync(dialog.FileName); _replacing = true; PopulateSlots(); SlotBox.SelectedIndex = 0; LoadSlot(0); }
        catch (Exception exception) { _loading = false; MessageBox.Show(this, exception.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        CommitSlot(_currentSlot);
        var dialog = new SaveFileDialog { Filter = "RTSPView configuration (*.json)|*.json", FileName = "RTSPView-config.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { await _store.ExportWithoutCredentialsAsync(_settings, dialog.FileName); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void PopulateSlots()
    {
        _indices = Enumerable.Range(0, _settings.CameraCount).Where(i => !_settings.DeletedCameraSlots.Contains(_settings.Cameras[i].Slot)).ToArray();
        SlotBox.ItemsSource = _indices.Select(i => $"{i + 1}  {_settings.Cameras[i].Name}").ToArray();
        EnabledBox.IsEnabled = NameBox.IsEnabled = UrlBox.IsEnabled = _indices.Length > 0;
    }
}
