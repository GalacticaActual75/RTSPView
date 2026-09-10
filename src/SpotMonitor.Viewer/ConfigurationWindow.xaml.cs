using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SpotMonitor.Core;
using SpotMonitor.Infrastructure;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace SpotMonitor.Viewer;

public partial class ConfigurationWindow : Window
{
    private readonly JsonSettingsStore _store;
    private AppSettings _settings;
    private int _currentSlot;
    private bool _loading;

    public AppSettings Settings => _settings;

    public ConfigurationWindow(AppSettings settings, JsonSettingsStore store)
    {
        InitializeComponent();
        _settings = settings.Normalize();
        _store = store;
        SlotBox.ItemsSource = Enumerable.Range(1, _settings.Cameras.Count).ToArray();
        SlotBox.SelectedIndex = 0;
        LoadSlot(0);
    }

    private void SlotBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _loading) return;
        CommitSlot(_currentSlot);
        LoadSlot(Math.Max(0, SlotBox.SelectedIndex));
    }

    private void LoadSlot(int index)
    {
        _loading = true;
        _currentSlot = index;
        var camera = _settings.Cameras[index];
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
        _loading = false;
    }

    private void CommitSlot(int index)
    {
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
            await _store.SaveAsync(_settings);
            DialogResult = true;
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Configuration not saved", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "SpotMonitor configuration (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try { _settings = await _store.ImportAsync(dialog.FileName); SlotBox.SelectedIndex = 0; LoadSlot(0); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        CommitSlot(_currentSlot);
        var dialog = new SaveFileDialog { Filter = "SpotMonitor configuration (*.json)|*.json", FileName = "spotmonitor-config.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { await _store.ExportWithoutCredentialsAsync(_settings, dialog.FileName); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
