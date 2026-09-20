using System.Reflection;
using RTSPView.Core;
using RTSPView.Viewer;

internal static class SensorViewerChecks
{
    public static void Run(MainWindow viewer)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainWindow);
        var settingsField = type.GetField("_settings", flags)!;
        var original = (AppSettings)settingsField.GetValue(viewer)!;
        var settings = original.Normalize();
        settings = settings with { DoorbellOverlay = settings.DoorbellOverlay with { Camera = settings.DoorbellOverlay.Camera with { RtspUrl = "rtsp://camera.example/live", Enabled = true } } };
        var layout = settings.Layouts[0] with { Id = Guid.NewGuid().ToString("N"), Name = "Sensor layout" };
        settings = settings with { Layouts = settings.Layouts.Append(layout).ToArray() };
        var hash = AutomationConfiguration.Hash(settings);
        settingsField.SetValue(viewer, settings);
        try
        {
            var apply = type.GetMethod("ApplySensorAutomation", flags)!;
            var overlayEnabled = type.GetMethod("OverlayEnabled", flags)!;
            var effective = type.GetProperty("EffectiveLayout", flags)!;
            void Send(params SensorEffect[] effects)
            {
                var result = (ViewerCommandResult)apply.Invoke(viewer, [new ViewerCommand(Guid.NewGuid(), ViewerCommandType.SensorAutomation,
                    Sensors: new(hash, DateTimeOffset.UtcNow.AddSeconds(5), effects))])!;
                if (!result.Success) throw new Exception(result.Message);
            }
            Send(new SensorEffect("door", SensorAction.HideOverlay, 10, "", 2));
            if ((bool)overlayEnabled.Invoke(viewer, [settings.DoorbellOverlay])!) throw new Exception("Sensor hide did not override enabled overlay");
            Send(new SensorEffect("door", SensorAction.ShowOverlay, 10, "", 2));
            if (!(bool)overlayEnabled.Invoke(viewer, [settings.DoorbellOverlay with { Camera = settings.DoorbellOverlay.Camera with { Enabled = false } }])!) throw new Exception("Sensor show did not override disabled overlay");
            Send(new SensorEffect("door", SensorAction.Layout, 0, layout.Id, 2));
            if (((WallLayout)effective.GetValue(viewer)!).Id != layout.Id) throw new Exception("Sensor layout was not wired into viewer");
            var detections = (OverlayAutomationState)type.GetField("_automationPresentation", flags)!.GetValue(viewer)!;
            detections.Update([new("high", 1, DateTimeOffset.UtcNow.AddSeconds(5), AutomationAction.FullScreen, Priority: 1)], DateTimeOffset.UtcNow);
            type.GetMethod("RefreshAutomationOverlays", flags)!.Invoke(viewer, null);
            if ((int?)type.GetProperty("EffectiveFocusedSlot", flags)!.GetValue(viewer) != 1) throw new Exception("Person priority did not interrupt sensor layout");
            detections.Clear(); Send();
            if (((WallLayout)effective.GetValue(viewer)!).Id != settings.ActiveLayoutId) throw new Exception("Sensor clear did not restore saved layout");
            if (!(bool)overlayEnabled.Invoke(viewer, [settings.DoorbellOverlay])!) throw new Exception("Sensor clear did not restore saved overlay setting");
            var stale = (ViewerCommandResult)apply.Invoke(viewer, [new ViewerCommand(Guid.NewGuid(), ViewerCommandType.SensorAutomation,
                Sensors: new("wrong-configuration", DateTimeOffset.UtcNow.AddSeconds(5), []))])!;
            if (stale.Success) throw new Exception("Stale sensor configuration accepted");
            Console.WriteLine("PASS real Viewer sensor IPC: show/hide overrides, saved layout, higher-priority person preemption, clear restoration and configuration rejection.");
        }
        finally { type.GetMethod("ClearAutomation", flags)!.Invoke(viewer, null); settingsField.SetValue(viewer, original); }
    }
}
