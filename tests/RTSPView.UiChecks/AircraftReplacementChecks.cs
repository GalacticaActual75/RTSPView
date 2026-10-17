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
            settingsField.SetValue(viewer, ((AppSettings)settingsField.GetValue(viewer)!) with { Plugins = new() { Aircraft = false } });
            sync.Invoke(viewer, null);
            if (camera.ContentOverlayRoot.Children.OfType<AircraftView>().Any()) throw new Exception("Disabled aircraft plugin left a native replacement visible");
            if (((AppSettings)settingsField.GetValue(viewer)!).Layouts[0].Tiles[0].Aircraft is null) throw new Exception("Disabling aircraft deleted its settings");
            var widgetLayer = (Canvas)viewer.FindName("LayoutWidgetLayer");
            var widthBinding = System.Windows.Data.BindingOperations.GetBindingBase(widgetLayer, FrameworkElement.WidthProperty)!;
            var heightBinding = System.Windows.Data.BindingOperations.GetBindingBase(widgetLayer, FrameworkElement.HeightProperty)!;
            try
            {
                widgetLayer.Width = 800; widgetLayer.Height = 450; widgetLayer.Measure(new Size(800,450)); widgetLayer.Arrange(new Rect(0,0,800,450));
                var widgetLayout = layout with { Widgets = [new WallWidget { Id = "free-aircraft", Kind = "aircraft", Aircraft = options, X = 100, Y = 100 }, new WallWidget { Id = "tile-weather", Kind = "weather", HostCameraSlot = 27, Weather = new() { Location = "Test", Latitude = 45, Longitude = -120 }, X = 0, Y = 0 }] };
                settingsField.SetValue(viewer, original with { Layouts = [widgetLayout], ActiveLayoutId = widgetLayout.Id }); snapshotsField.SetValue(viewer, new[] { snapshot });
                sync.Invoke(viewer, null);
                var floating = widgetLayer.Children.OfType<AircraftView>().Single();
                if (floating.Width <= 0 || Canvas.GetLeft(floating) + floating.Width * 800 / widgetLayout.EffectiveWidth > 800.01 || Canvas.GetTop(floating) + floating.Height * 800 / widgetLayout.EffectiveWidth > 450.01 || !widgetLayer.Children.OfType<WeatherView>().Any()) throw new Exception("Native free and anchored widgets did not fit layout");
                var resizedWidget = widgetLayout.Widgets[0] with { ContentScale = 1.5, WidthPercent = 60, HeightPercent = 50 };
                settingsField.SetValue(viewer, original with { Layouts = [widgetLayout with { Widgets = [resizedWidget] }], ActiveLayoutId = widgetLayout.Id }); sync.Invoke(viewer, null);
                var expectedBounds = resizedWidget.Bounds(widgetLayout.EffectiveWidth,widgetLayout.EffectiveHeight);
                if (Math.Abs(floating.Width * resizedWidget.ContentScaleFor(expectedBounds.Width,expectedBounds.Height) - expectedBounds.Width) > .01 || floating.LayoutTransform is not ScaleTransform transform || Math.Abs(transform.ScaleX - 800d/widgetLayout.EffectiveWidth*resizedWidget.ContentScaleFor(expectedBounds.Width,expectedBounds.Height)) > .001) throw new Exception("Native widget resize did not scale content and typography together");
                Console.WriteLine("PASS native widget resize scales content without rewriting text sizes.");
                settingsField.SetValue(viewer, original with { Layouts = [layout with { Id = "other" }], ActiveLayoutId = "other" }); sync.Invoke(viewer, null);
                if (widgetLayer.Children.Count != 0) throw new Exception("Layout widgets leaked into another layout");
                settingsField.SetValue(viewer, original with { Layouts = [widgetLayout], ActiveLayoutId = widgetLayout.Id, Plugins = new() { Aircraft = false, Weather = false } }); sync.Invoke(viewer, null);
                if (widgetLayer.Children.Count != 0) throw new Exception("Disabled plugins left free widgets visible");
                Console.WriteLine("PASS native independent widgets: bounded geometry, layout isolation and plugin gating.");
                var utilityLayout = layout with { Widgets = [new() { Id = "stats", Kind = "systemStats", SystemStats = new() }, new() { Id = "clock", Kind = "dateTime", DateTime = new() }] };
                settingsField.SetValue(viewer, original with { Layouts = [utilityLayout], ActiveLayoutId = layout.Id, Plugins = Plugins.ForNewInstall with { SystemStats = true, DateTime = true } }); sync.Invoke(viewer, null);
                if (widgetLayer.Children.OfType<SystemWidgetView>().Count() != 2) throw new Exception("Native system widgets require unrelated plugins to render.");
                settingsField.SetValue(viewer, original with { Layouts = [utilityLayout], ActiveLayoutId = layout.Id, Plugins = Plugins.ForNewInstall }); sync.Invoke(viewer, null);
                if (widgetLayer.Children.Count != 0) throw new Exception("Disabled system widgets remain on the wall.");
                Console.WriteLine("PASS native system and clock widgets render independently and disappear when their plugins are disabled.");
            }
            finally { widgetLayer.SetBinding(FrameworkElement.WidthProperty, widthBinding); widgetLayer.SetBinding(FrameworkElement.HeightProperty, heightBinding); }
            Console.WriteLine("PASS disabled aircraft removes native presentation while preserving tile configuration.");
            // Exercise an actual owned HWND, not just a Canvas in the WPF visual tree.
            var loaded = (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), viewer, type.GetMethod("OnLoaded", flags, null, [typeof(object), typeof(RoutedEventArgs)], null)!);
            viewer.Loaded -= loaded;
            try
            {
                var freeLayout = layout with { Widgets = [new WallWidget { Id = "native-overlay", Kind = "aircraft", Aircraft = options }] };
                settingsField.SetValue(viewer, original with { Layouts = [freeLayout], ActiveLayoutId = freeLayout.Id });
                snapshotsField.SetValue(viewer, new[] { snapshot });
                viewer.Show(); viewer.UpdateLayout(); sync.Invoke(viewer, null);
                var overlay = (Window?)type.GetField("_layoutWidgetWindow", flags)!.GetValue(viewer);
                if (overlay is null || !overlay.IsVisible || overlay.Owner != viewer || !overlay.AllowsTransparency || overlay.ShowActivated || overlay.IsHitTestVisible)
                    throw new Exception("Independent widgets lack a visible non-activating owned overlay window");
                if (System.Windows.PresentationSource.FromVisual(widgetLayer) != System.Windows.PresentationSource.FromVisual(overlay))
                    throw new Exception("Widget Canvas remained behind native camera windows");
                viewer.WindowState = WindowState.Minimized;
                if (overlay.IsVisible) throw new Exception("Widget overlay remained visible after minimizing Live View");
                viewer.WindowState = WindowState.Normal; viewer.UpdateLayout(); sync.Invoke(viewer, null);
                if (!overlay.IsVisible) throw new Exception("Widget overlay did not return after restoring Live View");
                settingsField.SetValue(viewer, original with { Layouts = [layout with { Widgets = [] }], ActiveLayoutId = layout.Id });
                sync.Invoke(viewer, null);
                if (overlay.IsVisible) throw new Exception("Empty layout left the widget overlay window visible");
                Console.WriteLine("PASS native widget HWND, ownership, transparency, minimize/restore and empty-layout hiding.");
            }
            finally { viewer.Hide(); viewer.Loaded += loaded; }
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
        var label = Descendants(view).OfType<TextBlock>().SingleOrDefault(t => t.Text == "TEST27" && t.FontWeight == FontWeights.SemiBold);
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
