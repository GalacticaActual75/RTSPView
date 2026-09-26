using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RTSPView.Core;
using RTSPView.Viewer;

internal static class AircraftReplacementChecks
{
    public static void Run(MainWindow viewer)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainWindow);
        var settingsField = type.GetField("_settings", flags)!;
        var tilesField = type.GetField("_tiles", flags)!;
        var snapshotsField = type.GetField("_aircraftSnapshots", flags)!;
        var timerField = type.GetField("_aircraftTimer", flags)!;
        var sync = type.GetMethod("SyncAircraft", flags)!;
        var original = (AppSettings)settingsField.GetValue(viewer)!;
        var oldTiles = tilesField.GetValue(viewer); var oldSnapshots = snapshotsField.GetValue(viewer); var oldTimer = timerField.GetValue(viewer);
        var camera = new CameraTile();
        typeof(CameraTile).GetField("_settings", flags)!.SetValue(camera, new CameraSettings { Slot = 27, Enabled = false });
        var host = new Window { Content = camera, Width = 640, Height = 360, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            var options = new AircraftOptions { ShowPhoto = false, Latitude = 45, Longitude = -120, BackgroundOpacity = 35,
                Fields = ["type", "owner", "airline", "destination", "altitude", "speed", "distance", "track", "verticalRate"] };
            var layout = original.Layouts[0] with { Rows = 1, Columns = 1, Tiles = [new() { CameraSlot = 27, Aircraft = options }] };
            settingsField.SetValue(viewer, original with { Layouts = [layout], ActiveLayoutId = layout.Id });
            tilesField.SetValue(viewer, new[] { camera }); timerField.SetValue(viewer, new DispatcherTimer());
            var snapshot = new AircraftSnapshot { Key = options.CacheKey, FetchedAt = DateTimeOffset.UtcNow,
                Aircraft = [new() { Hex = "a12345", Callsign = "TEST27", Type = "E75L", Registration = "N123EX", RegisteredOwner = "Example Airlines", Airline = "Example Airlines", Destination = "JFK · New York", AltitudeFeet = 34000, SpeedKnots = 387, Latitude = 45.001, Longitude = -120, PositionAt = DateTimeOffset.UtcNow }] };
            host.Show(); camera.SetWallVisibility(true);
            snapshotsField.SetValue(viewer, new[] { snapshot }); sync.Invoke(viewer, null); host.UpdateLayout();
            var view = camera.ContentOverlayRoot.Children.OfType<AircraftView>().Single();
            if (!view.IsVisible || view.ActualWidth < 100 || view.ActualHeight < 100) throw new Exception("Real viewer did not show replacement on camera #27");
            AssertCallsign(view);
            if (((SolidColorBrush)view.Background).Color.A != 35 * 255 / 100)
                throw new Exception("Aircraft camera overlay ignored configured background opacity");
            foreach (var height in new[] { 280d, 220d, 180d, 360d })
            {
                host.Height = height; host.UpdateLayout(); sync.Invoke(viewer, null); host.UpdateLayout();
                AssertCallsign(view);
            }
            snapshotsField.SetValue(viewer, new[] { snapshot with { Aircraft = [] } }); sync.Invoke(viewer, null);
            if (view.Visibility != Visibility.Collapsed) throw new Exception("Real viewer did not restore camera #27 for empty traffic");
            snapshotsField.SetValue(viewer, new[] { snapshot }); sync.Invoke(viewer, null);
            if (view.Visibility != Visibility.Visible) throw new Exception("Real viewer did not restore replacement after traffic returned");
            host.UpdateLayout();
            AssertCallsign(view);
            snapshotsField.SetValue(viewer, new[] { snapshot with { RefreshFailed = true } }); sync.Invoke(viewer, null);
            if (view.Visibility != Visibility.Collapsed) throw new Exception("Real viewer did not restore camera #27 for failed feed");
            Console.WriteLine("PASS real MainWindow aircraft replacement on slot 27: visible with size, empty return, fresh reactivation and failed-feed return.");
        }
        finally
        {
            host.Close(); settingsField.SetValue(viewer, original); tilesField.SetValue(viewer, oldTiles);
            snapshotsField.SetValue(viewer, oldSnapshots); timerField.SetValue(viewer, oldTimer);
            ((Dictionary<int, AircraftView>)type.GetField("_aircraftReplacements", flags)!.GetValue(viewer)!).Clear();
        }
    }
    private static void AssertCallsign(AircraftView view)
    {
        var label = Descendants(view).OfType<TextBlock>().SingleOrDefault(t => t.Text == "TEST27");
        if (label is null || !label.IsVisible || label.ActualWidth < 10 || label.ActualHeight < 10)
            throw new Exception("Aircraft replacement has no visibly laid-out callsign");
        var bounds = label.TransformToAncestor(view).TransformBounds(new Rect(label.RenderSize));
        if (!new Rect(view.RenderSize).Contains(bounds)) throw new Exception("Aircraft callsign is clipped outside its replacement card");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
