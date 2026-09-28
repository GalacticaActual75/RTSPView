using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RTSPView.Core;
using RTSPView.Infrastructure;
using RTSPView.Viewer;
using RTSPView.Controller;
using RTSPView.Hardware;

internal static class SystemWidgetChecks
{
    public static void Run()
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
        var now = new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero);
        var clock = new DateTimeOptions { TimeZone = "America/Los_Angeles", Use24Hour = true, DateFormat = "iso" };
        clock.Validate();
        Check(clock.Format(now.AddSeconds(-1)).Time == "01:59:59" && clock.Format(now).Time == "03:00:00", "clock respects daylight-saving transition");
        Check((clock with { Use24Hour = false }).Format(now).Time == "3:00:00 AM", "clock supports 12-hour time");
        Check((clock with { ShowWeekday = false }).Format(now).Date == "2026-03-08", "clock ISO date uses selected zone");
        Check((clock with { ShowDate = false, ShowWeekday = false }).Format(now).Date == "", "clock can reclaim both date and weekday rows");
        Check(new SystemStatsOptions { TemperatureUnit = "fahrenheit" }.Temperature(20) == "68°F", "system temperatures convert correctly");
        Check(new SystemStatsOptions().Temperature(null) == "—" && !SystemStatsOptions.IsFresh(new() { Timestamp = now.AddSeconds(-11) }, now), "missing and stale system readings stay unavailable");
        Check(new WeatherOptions().ContentOpacity == 100 && new AircraftOptions().ContentOpacity == 100, "legacy widget content stays fully visible");
        foreach (var opacity in new[] { -1, 101 })
        {
            var rejected = false;
            try { new WeatherOptions { ContentOpacity = opacity }.Validate(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "out-of-range content opacity rejected");
        }
        var directory = Path.Combine(Path.GetTempPath(), "RTSPView-SystemWidgets-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
            var fresh = store.LoadAsync().GetAwaiter().GetResult();
            Check(fresh.Plugins == Plugins.ForNewInstall, "all plugins are disabled on new installs");
            var legacy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"SchemaVersion\":21}")!.Normalize();
            Check(legacy.Plugins.Weather && legacy.Plugins.Aircraft && legacy.Plugins.Automations && !legacy.Plugins.SystemStats && !legacy.Plugins.DateTime, "legacy missing flags preserve existing plugins and leave new ones disabled");
            var layout = fresh.Layouts[0] with { Widgets = [new() { Id = "stats", Kind = "systemStats", SystemStats = new() { Appearance = new() { ContentOpacity = 35, BackgroundOpacity = 70 } } }, new() { Id = "clock", Kind = "dateTime", DateTime = clock }] };
            store.SaveAsync(fresh with { Layouts = [layout] }).GetAwaiter().GetResult();
            var loaded = store.LoadAsync().GetAwaiter().GetResult();
            Check(loaded.Layouts[0].Widgets.Count == 2 && loaded.Layouts[0].Widgets[1].DateTime!.TimeZone == clock.TimeZone, "disabled system and clock widgets survive settings round trip");
            Check(loaded.Layouts[0].Widgets[0].SystemStats!.Appearance is { ContentOpacity: 35, BackgroundOpacity: 70 }, "background and content opacity persist independently");
            var rejected = false;
            try { (layout.Widgets[0] with { Weather = new() }).Validate(layout); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "mixed widget kinds are rejected");
        }
        finally { Directory.Delete(directory, true); }
        ServiceChecks().GetAwaiter().GetResult();
        var wall = new StackPanel { Width = 640 };
        var weather = new WeatherView { Width = 640, Height = 360 };
        weather.Update(new() { ContentOpacity = 35, BackgroundOpacity = 70 }, null);
        weather.Measure(new Size(640, 360)); weather.Arrange(new Rect(0, 0, 640, 360)); weather.UpdateLayout();
        Check(Math.Abs(weather.Child.Opacity - .35) < .001 && weather.Opacity == 1 && ((SolidColorBrush)weather.Background).Color.A == (byte)(70 * 255 / 100), "native weather content fades independently of background");
        var aircraft = new AircraftView { Width = 640, Height = 360 };
        aircraft.Update(new() { ContentOpacity = 0, BackgroundOpacity = 80 }, null);
        Check(aircraft.Child.Opacity == 0 && ((SolidColorBrush)aircraft.Background).Color.A > 0, "native aircraft content can be transparent while background remains");
        foreach (var kind in new[] { "systemStats", "dateTime" })
        {
            var view = new SystemWidgetView { Width = 640, Height = 260 };
            view.Update(new() { Kind = kind, SystemStats = kind == "systemStats" ? new() : null, DateTime = kind == "dateTime" ? clock : null });
            wall.Children.Add(view);
            foreach (var size in new[] { new Size(160, 96), new Size(320, 180), new Size(640, 360) })
            {
                view.Width = size.Width; view.Height = size.Height; view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
                Check(view.DesiredSize.Width <= size.Width && view.DesiredSize.Height <= size.Height, kind + " fits " + size);
            }
            view.Width = 640; view.Height = 260;
        }
        wall.Measure(new Size(640, 520)); wall.Arrange(new Rect(0, 0, 640, 520)); wall.UpdateLayout();
        var bitmap = new RenderTargetBitmap(640, 520, 96, 96, PixelFormats.Pbgra32); bitmap.Render(wall);
        Directory.CreateDirectory("artifacts/widgets"); using var file = File.Create("artifacts/widgets/native-system-widgets.png"); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
    }
    private sealed class Sensors : ITemperatureSensors
    {
        public (double? Cpu, double? Gpu) Read() => (58, 64);
        public void Dispose() { }
    }
    private static async Task ServiceChecks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RTSPView-SystemService-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"));
            await store.SaveAsync(new() { Plugins = Plugins.ForNewInstall });
            using var metrics = new SystemMetricsCollector();
            using var temperatures = new TemperatureMonitor(directory, new Sensors(), _ => { });
            await temperatures.SampleAsync(DateTimeOffset.UtcNow);
            using var service = new SystemStatsService(directory, metrics, temperatures, new RollingFileLogger(Path.Combine(directory, "logs")));
            await service.StartAsync(CancellationToken.None);
            try
            {
                var path = Path.Combine(directory, "system-stats.json");
                await Task.Delay(1100);
                if (File.Exists(path)) throw new Exception("Disabled plugin published readings.");
                await store.SaveAsync((await store.LoadAsync()) with { Plugins = Plugins.ForNewInstall with { SystemStats = true } });
                for (var i = 0; i < 100 && !File.Exists(path); i++) await Task.Delay(50);
                if (service.Latest is not { CpuTemperatureC: 58, GpuTemperatureC: 64 } || !File.Exists(path)) throw new Exception("Enabled stats did not publish shared temperature readings.");
                await store.SaveAsync((await store.LoadAsync()) with { Plugins = Plugins.ForNewInstall });
                await Task.Delay(1200);
                var before = File.ReadAllText(path);
                await Task.Delay(2200);
                if (before != File.ReadAllText(path)) throw new Exception("Disabled stats continued publishing.");
                Console.WriteLine("PASS system stats starts and stops with its plugin, sharing existing temperature readings");
            }
            finally { await service.StopAsync(CancellationToken.None); }
        }
        finally { Directory.Delete(directory, true); }
    }
}
