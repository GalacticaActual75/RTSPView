using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RTSPView.Viewer;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var result = 1;
        var app = new Application();
        var wall = new Grid();
        for (var i = 0; i < 4; i++) { wall.RowDefinitions.Add(new()); wall.ColumnDefinitions.Add(new()); }
        var tiles = Enumerable.Range(0, 16).Select(_ => new CameraTile { Visibility = Visibility.Collapsed }).ToArray();
        foreach (var tile in tiles) wall.Children.Add(tile);
        var window = new Window { Content = wall, Width = 800, Height = 600, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Loaded += async (_, _) =>
        {
            try
            {
                await SetLayout(9);
                Check(9, "startup 3x3 hides seven unused status windows");
                NativeBackgroundChecks.Run(tiles[0]);
                await SetLayout(16);
                Check(16, "expanded layout restores all status windows");
                await SetLayout(1);
                Check(1, "switching to one camera hides fifteen status windows");
                await SetLayout(9);
                Check(9, "returning to 3x3 restores assigned status windows");
                NativeBackgroundChecks.Run(tiles[0]);
                await NativeStartupChecks.Run();
                result = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { window.Close(); app.Shutdown(); }
        };
        app.Run(window);
        return result;

        async Task SetLayout(int count)
        {
            for (var i = 0; i < tiles.Length; i++)
            {
                Grid.SetRow(tiles[i], i / 4); Grid.SetColumn(tiles[i], i % 4);
                tiles[i].SetWallVisibility(i < count);
            }
            wall.UpdateLayout();
            await wall.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(100);
        }

        void Check(int count, string message)
        {
            for (var i = 0; i < tiles.Length; i++)
            {
                var label = (FrameworkElement)tiles[i].FindName("StateText");
                var overlay = (FrameworkElement)tiles[i].FindName("OverlayRoot");
                var statusWindow = Window.GetWindow(overlay);
                if (label.IsVisible != (i < count)) throw new Exception($"Unexpected label visibility at tile {i + 1}: {message}");
                if (i >= count && statusWindow?.IsVisible == true) throw new Exception($"Unused status window visible at tile {i + 1}: {message}");
            }
            Console.WriteLine("PASS " + message);
        }
    }
}
