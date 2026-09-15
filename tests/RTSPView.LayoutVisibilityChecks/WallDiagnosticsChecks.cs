using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RTSPView.Core;
using RTSPView.Viewer;

internal static class WallDiagnosticsChecks
{
    public static void Run()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var main = new CameraTile { SharedDiagnostics = true };
        var overlay = new CameraTile { SharedDiagnostics = true };
        var settings = typeof(CameraTile).GetField("_settings", flags)!;
        var state = typeof(CameraTile).GetField("_status", flags)!;
        settings.SetValue(main, new CameraSettings { Slot = 1, Name = "Front door", RtspUrl = "rtsp://example.test/main" });
        settings.SetValue(overlay, new CameraSettings { Slot = 17, Name = "Front door", RtspUrl = "rtsp://example.test/overlay" });
        typeof(CameraTile).GetField("_useCompositedOutput", flags)!.SetValue(overlay, true);
        // These fixtures have no decoder; exercise connection state separately
        // from the frame watchdog tested by StreamTelemetryChecks.
        typeof(CameraTile).GetField("_pendingNativeStart", flags)!.SetValue(main, true);
        typeof(CameraTile).GetField("_pendingNativeStart", flags)!.SetValue(overlay, true);
        state.SetValue(main, new CameraRuntimeStatus { State = CameraConnectionState.Reconnecting });
        state.SetValue(overlay, new CameraRuntimeStatus { State = CameraConnectionState.StreamError, LastError = "Decoder could not recover. Retrying the stream." });
        var panel = new WallDiagnosticsPanel();
        panel.Refresh([main, overlay]);
        if (panel.Visibility != Visibility.Collapsed) throw new Exception("Windowed diagnostics opened without a click");
        panel.SelectCamera(17);
        if (panel.Visibility != Visibility.Collapsed) throw new Exception("Hover opened windowed diagnostics");
        panel.ToggleWindowed();
        if (panel.Visibility != Visibility.Visible) throw new Exception("Diagnostics button did not open windowed panel");
        if (panel.WarningCount != 2) throw new Exception("Simultaneous main/overlay errors were not preserved");
        if (!overlay.DiagnosticWarning!.Contains("Decoder could not recover")) throw new Exception("Connection error lost its explanatory text");
        panel.SelectCamera(17, true);
        if (panel.SelectedSlot != 17 || !overlay.DiagnosticLabel.Contains("Overlay") || !main.DiagnosticLabel.Contains("Main feed"))
            throw new Exception("Duplicate camera names were not distinguished by feed type and slot");
        var present = typeof(CameraTile).GetMethod("UpdateOverlayPresentation", flags)!;
        present.Invoke(overlay, [DateTimeOffset.UtcNow]);
        foreach (var name in new[] { "OverlayPanel", "StaleBanner", "StateText", "SlotText" })
            if (((UIElement)overlay.FindName(name)).Visibility != Visibility.Collapsed)
                throw new Exception("Shared diagnostics left clipped text in the overlay: " + name);
        if (((UIElement)overlay.FindName("DiagnosticBadge")).Visibility != Visibility.Visible)
            throw new Exception("Affected camera has no warning badge");
        var requested = false;
        overlay.DiagnosticsRequested += (_, _) => requested = true;
        ((Button)overlay.FindName("DiagnosticBadge")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!requested) throw new Exception("Camera warning badge did not request diagnostics");

        var grid = new Grid { Background = Brushes.Black, Width = 900, Height = 620 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var picture = new Border { Background = Brushes.DimGray, Margin = new Thickness(20), CornerRadius = new CornerRadius(200),
            Child = new TextBlock { Text = "Shaped camera picture\nDiagnostics stay outside this region", Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        grid.Children.Add(picture); Grid.SetColumn(panel, 1); grid.Children.Add(panel);
        grid.Measure(new Size(900, 620)); grid.Arrange(new Rect(0, 0, 900, 620)); grid.UpdateLayout();
        if (picture.TranslatePoint(new Point(picture.ActualWidth, 0), grid).X > panel.TranslatePoint(new Point(), grid).X)
            throw new Exception("Reserved diagnostics area overlaps the video area");
        var bitmap = new RenderTargetBitmap(900, 620, 96, 96, PixelFormats.Pbgra32); bitmap.Render(grid);
        var path = Path.Combine(Path.GetTempPath(), "rtspview-shared-diagnostics.png");
        using (var stream = File.Create(path)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream); }
        Console.WriteLine("Diagnostics preview: " + path);
        var toggle = (Button)((DockPanel)panel.Content).Children[0];
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        panel.Refresh([main, overlay]);
        if (panel.Visibility != Visibility.Collapsed) throw new Exception("Existing errors reopened a closed windowed panel");
        panel.SetFullScreen(true);
        if (panel.Visibility != Visibility.Visible || toggle.Visibility != Visibility.Collapsed)
            throw new Exception("Full screen did not show current errors automatically");
        panel.Refresh([main, overlay], [1,17]);
        if(panel.Visibility != Visibility.Collapsed || panel.WarningCount != 2) throw new Exception("Exclusions must suppress automatic opening without removing alerts");
        panel.Refresh([main, overlay], [17]);
        if(panel.Visibility != Visibility.Visible) throw new Exception("Non-excluded issue must still open diagnostics");
        panel.SetFullScreen(false); panel.ToggleWindowed();
        if(panel.Visibility != Visibility.Visible || panel.WarningCount != 2) throw new Exception("Manual diagnostics must include excluded cameras");
        panel.ToggleWindowed(); panel.SetFullScreen(true);
        state.SetValue(overlay, new CameraRuntimeStatus { State = CameraConnectionState.Live });
        panel.Refresh([main, overlay]);
        if (panel.WarningCount != 1 || panel.SelectedSlot != 17) throw new Exception("Recovery lost the other camera's alert or changed selection");
        if (panel.Visibility != Visibility.Visible) throw new Exception("Partial recovery hid remaining errors");
        state.SetValue(main, new CameraRuntimeStatus { State = CameraConnectionState.Live });
        panel.Refresh([main, overlay]);
        if (panel.Visibility != Visibility.Collapsed) throw new Exception("Full screen retained diagnostics after all errors cleared");
        grid.Measure(new Size(900, 620)); grid.Arrange(new Rect(0, 0, 900, 620)); grid.UpdateLayout();
        if (grid.ColumnDefinitions[1].ActualWidth != 0) throw new Exception("Hidden diagnostics left a reserved strip");
        panel.SelectCamera(17, true);
        if (panel.Visibility != Visibility.Collapsed) throw new Exception("Manual selection opened healthy full-screen diagnostics");
        state.SetValue(overlay, new CameraRuntimeStatus { State = CameraConnectionState.StreamError });
        panel.Refresh([main, overlay]);
        if (panel.Visibility != Visibility.Visible) throw new Exception("New connection problem did not open full-screen diagnostics");
        panel.SetFullScreen(false);
        if (panel.Visibility != Visibility.Collapsed) throw new Exception("Leaving full screen opened windowed diagnostics without a click");
        panel.ToggleWindowed();
        panel.SetFullScreen(true);
        state.SetValue(overlay, new CameraRuntimeStatus { State = CameraConnectionState.Live });
        panel.Refresh([main, overlay]);
        if (panel.Visibility != Visibility.Collapsed) throw new Exception("Windowed preference kept healthy full-screen panel visible");
        panel.SetFullScreen(false);
        if (panel.Visibility != Visibility.Visible) throw new Exception("Leaving full screen lost the windowed open preference");
        panel.Refresh([main]);
        if (panel.SelectedSlot != 1) throw new Exception("Removed overlay left a stale selected camera");
        main.Dispose(); overlay.Dispose();
        Console.WriteLine("PASS shared diagnostics: simultaneous errors, feed identity, unclipped text, warning badge, fullscreen auto-show/recovery, windowed toggle, reclaimed space and removal");
    }
}
