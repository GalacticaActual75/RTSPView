using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RTSPView.Core;
using Screen = System.Windows.Forms.Screen;

namespace RTSPView.Viewer;

internal static class DisplayMonitors
{
    public static DisplayMonitor[] Current() => Screen.AllScreens.Select((screen, index) => new DisplayMonitor(index, screen.DeviceName,
        $"Display {index + 1} — {screen.DeviceName} — {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " — Primary" : ""),
        screen.Primary, screen.Bounds.Width, screen.Bounds.Height)).ToArray();
    public static int Selected(AppSettings settings, Screen[] screens)
    {
        var named = Array.FindIndex(screens, s => s.DeviceName.Equals(settings.PreferredMonitorDevice, StringComparison.OrdinalIgnoreCase));
        return named >= 0 ? named : Math.Clamp(settings.PreferredMonitor, 0, Math.Max(0, screens.Length - 1));
    }
    private static readonly List<Window> Badges = [];
    public static void Identify()
    {
        foreach (var badge in Badges.ToArray()) badge.Close();
        Badges.Clear();
        foreach (var display in Current())
        {
            var screen = Screen.AllScreens[display.Index];
            var badge = new Window { Width = 360, Height = 130, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false, Topmost = true, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 33, 48)),
                Content = new TextBlock { Text = $"Display {display.Index + 1}\n{display.DeviceName}\n{display.Width} × {display.Height}",
                    FontSize = 24, Foreground = System.Windows.Media.Brushes.White, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            badge.SourceInitialized += (_, _) => {
                var dpi = VisualTreeHelper.GetDpi(badge);
                badge.Left = (screen.Bounds.Left + 40) / dpi.DpiScaleX;
                badge.Top = (screen.Bounds.Top + 40) / dpi.DpiScaleY;
            };
            Badges.Add(badge); badge.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => { timer.Stop(); badge.Close(); Badges.Remove(badge); };
            timer.Start();
        }
    }
}
