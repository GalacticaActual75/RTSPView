using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using RTSPView.Core;
using RTSPView.Viewer;

internal static class AircraftFadeChecks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Until(Func<bool> condition, string message)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && elapsed.Elapsed < TimeSpan.FromSeconds(5)) Pump(10);
        Check(condition(), message);
    }
    public static void Run()
    {
        var view = new AircraftView { Width = 800, Height = 450 };
        var host = new Window { Content = view, Width = 820, Height = 490, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        host.Show(); host.UpdateLayout(); Pump(50);
        // Leave time to observe an intermediate frame on a cold hosted Windows runner.
        // Assertions wait for the actual animation state, not a fixed wall-clock sample.
        var o = new AircraftOptions { Latitude = 45, Longitude = -120, CardDesign = "board", ShowPhoto = false, FadeInMilliseconds = 500, FadeOutMilliseconds = 1000 };
        var tracks = Enumerable.Range(1, 4).Select(i => new AircraftTrack { Hex = "abc00" + i, Callsign = "TEST" + i, Latitude = 45.001 + i * .001, Longitude = -120, PositionAt = DateTimeOffset.UtcNow }).ToArray();
        var one = new AircraftSnapshot { Key = o.CacheKey, FetchedAt = DateTimeOffset.UtcNow, Aircraft = [tracks[0]] };
        view.Measure(new Size(800, 450)); view.Arrange(new Rect(0, 0, 800, 450));
        view.Update(o, one);
        o = o with { FadeEnabled = true };
        view.Update(o, one);
        var old = view.Child;
        view.Update(o, one with { Aircraft = tracks[..2] });
        Check(ReferenceEquals(old, view.Child), "One-to-two must retain old content during fade-out");
        Until(() => view.Opacity < 1 && view.Opacity > 0 && ReferenceEquals(old, view.Child), "Outgoing card must actually fade while retaining its content");
        view.Update(o, one with { Aircraft = tracks[..2], FetchedAt = DateTimeOffset.UtcNow });
        Check(ReferenceEquals(old, view.Child), "Polling must not replace outgoing content");
        Until(() => !ReferenceEquals(old, view.Child) && view.Opacity == 1, "Pair must fade in and settle");
        // Force the real rotation deadline, avoiding a twenty-second test wait.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var rotation = typeof(AircraftView).GetField("_rotation", flags)!.GetValue(view)!;
        typeof(AircraftRotation).GetField("_changed", flags)!.SetValue(rotation, DateTimeOffset.UtcNow.AddSeconds(-21));
        old = view.Child; view.Update(o, one with { Aircraft = tracks });
        Check(ReferenceEquals(old, view.Child), "Pair rotation must retain outgoing content");
        Until(() => !ReferenceEquals(old, view.Child) && view.Opacity == 1, "Rotated pair must finish fade-in");
        old = view.Child; view.Update(o, one);
        Check(ReferenceEquals(old, view.Child), "Two-to-one must fade too");
        view.Update(o, one with { Aircraft = [] }, takeover: true);
        Until(() => view.Visibility == Visibility.Collapsed, "Hiding during a swap must cancel stale completion");
        Pump(o.FadeOutMilliseconds + o.FadeInMilliseconds + 100);
        Check(view.Visibility == Visibility.Collapsed, "Canceled content swap must not reveal stale content later");
        view.Update(o with { FadeEnabled = false }, one);
        Check(view.Visibility == Visibility.Visible && view.Opacity == 1, "Disabling fades must restore immediate display");
        host.Close();
        Console.WriteLine("PASS native aircraft fade timing, one/two transitions, pair rotation, refresh coalescing and cancellation.");
    }
}
