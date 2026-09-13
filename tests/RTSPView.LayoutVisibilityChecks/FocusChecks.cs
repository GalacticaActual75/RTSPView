using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RTSPView.Core;
using RTSPView.Viewer;

internal static class FocusChecks
{
    public static async Task Run(Grid wall, CameraTile[] tiles)
    {
        var layout = new WallLayout { Rows = 2, Columns = 3, Tiles = [
            new WallTile { CameraSlot = 1, ColumnSpan = 2, RowSpan = 2 },
            new WallTile { CameraSlot = 2, Column = 2 },
            new WallTile { CameraSlot = 3, Row = 1, Column = 2 }] };
        CameraWallPresentation.Apply(wall, tiles, layout, 2);
        wall.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(tiles[1].IsVisible && tiles.Where((_,i)=>i!=1).All(t=>!t.IsVisible), "only focused camera visible");
        Check(wall.RowDefinitions.Count == 1 && wall.ColumnDefinitions.Count == 1, "focus fills the wall");
        CameraWallPresentation.Apply(wall, tiles, layout, null);
        wall.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(tiles.Take(3).All(t=>t.IsVisible) && tiles.Skip(3).All(t=>!t.IsVisible), "restore visibility");
        Check(Grid.GetColumnSpan(tiles[0]) == 2 && Grid.GetRowSpan(tiles[0]) == 2 && Grid.GetColumn(tiles[2]) == 2, "restore custom geometry");
        Check(layout.Rows == 2 && layout.Tiles.Count == 3, "saved layout is unchanged");
        var settings = new AppSettings { CameraCount = 16, Layouts = [layout], ActiveLayoutId = layout.Id };
        settings = settings with { Cameras = settings.Cameras.Select(c => c with { Enabled = true, RtspUrl = "rtsp://example.test/" + c.Slot }).ToArray() };
        var focusedLayout = AutomationConfiguration.FocusedLayout(settings, 2);
        CameraWallPresentation.Apply(wall, tiles, focusedLayout, null);
        wall.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(tiles.All(t => t.IsVisible), "focused layout retains all sixteen streams");
        Check(Grid.GetRowSpan(tiles[1]) == 2 && Grid.GetColumnSpan(tiles[1]) == 2, "active camera receives larger geometry");
        CameraWallPresentation.Apply(wall, tiles, focusedLayout, 3);
        wall.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(tiles[2].IsVisible && tiles.Where((_, i) => i != 2).All(t => !t.IsVisible), "fullscreen action overrides focused layout");
        CameraWallPresentation.Apply(wall, tiles, layout, null);
        wall.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(tiles.Take(3).All(t => t.IsVisible) && tiles.Skip(3).All(t => !t.IsVisible) && Grid.GetColumn(tiles[2]) == 2, "focus expiry restores original custom geometry");
        Console.WriteLine("PASS temporary focus layout with all sixteen streams, fullscreen priority and original custom layout restoration");
        var overlay = (UIElement)tiles[0].FindName("OverlayRoot");
        tiles[0].SetOverlaySuppressed(true);tiles[0].Tick();
        if(overlay.Visibility!=Visibility.Collapsed)throw new Exception("Stream status overlay appeared over a modal dialog");
        tiles[0].SetOverlaySuppressed(false);
        if(overlay.Visibility!=Visibility.Visible)throw new Exception("Stream status overlay did not return after closing a dialog");
        var clicks = 0; tiles[0].FocusRequested += (_,_)=>clicks++;
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
        typeof(MouseButtonEventArgs).GetProperty("ClickCount")!.SetValue(args, 2);
        overlay.RaiseEvent(args);
        Check(clicks == 1, "double-click reaches focus handler through detached video overlay");
        var button = (Button)tiles[0].FindName("RestartStreamButton");
        var buttonArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
        typeof(MouseButtonEventArgs).GetProperty("ClickCount")!.SetValue(buttonArgs, 2);
        button.RaiseEvent(buttonArgs);
        Check(clicks == 1, "restart button double-click does not focus");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var past = DateTimeOffset.UtcNow.AddSeconds(-24);
        var activity = new StreamActivity(); activity.BeginAttempt(past.AddSeconds(-1)); activity.Observe(1, past);
        typeof(CameraTile).GetField("_activity", flags)!.SetValue(tiles[0], activity);
        typeof(CameraTile).GetField("_attemptStartedAt", flags)!.SetValue(tiles[0], past);
        typeof(CameraTile).GetField("_settings", flags)!.SetValue(tiles[0], new CameraSettings { Enabled = true, RtspUrl = "rtsp://camera.example/test" });
        typeof(CameraTile).GetField("_status", flags)!.SetValue(tiles[0], new CameraRuntimeStatus { State = CameraConnectionState.Live });
        tiles[0].ApplyOverlayPreferences(false, false);
        Check(((Border)tiles[0].FindName("StaleBanner")).IsVisible, "stale warning survives hidden labels and stats");
        Check(((TextBlock)tiles[0].FindName("StaleText")).Text.Contains("Last frame received"), "stale warning explains frame age");
        activity.Observe(2, DateTimeOffset.UtcNow); tiles[0].ApplyOverlayPreferences(false, false);
        Check(((Border)tiles[0].FindName("StaleBanner")).Visibility == Visibility.Collapsed, "fresh frame clears warning");
        Console.WriteLine("PASS focus, custom-layout restoration and detached-overlay double-click");
        Console.WriteLine("PASS stale banner with labels hidden, recovery and restart-button isolation");
    }
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); }
}
