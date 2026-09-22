using RTSPView.Core;
namespace RTSPView.Controller;

public sealed record ConfigurationLink(string Kind, string Id, string Name);
public static class ConfigurationLinks
{
    public static ConfigurationLink[] Stream(AppSettings settings, AutomationService mqtt, TapoService tapo, int slot, bool overlay)
    {
        var slots = overlay ? new[] { slot, StreamCatalog.SourceSlot(slot) } : new[] { slot };
        var links = mqtt.CurrentSettings.Rules.Where(r => r.Sources.Any(s => slots.Contains(s.CameraSlot)) ||
            (r.Action == AutomationAction.Overlay ? overlay && r.OverlaySlot == slot : slots.Contains(r.CameraSlot) || slots.Contains(r.SecondCameraSlot)))
            .Select(r => new ConfigurationLink("mqtt", r.Id, r.Name)).ToList();
        links.AddRange(tapo.CurrentSettings.Rules.Where(r =>
            ((r.Action == SensorAction.AutomationLayout || r.ClearAction == SensorAction.AutomationLayout) && (slots.Contains(r.FocusCameraSlot) || slots.Contains(r.SecondFocusCameraSlot))) ||
            (overlay && r.OverlaySlot == slot && new[] {r.Action,r.ClearAction,r.UnavailableAction}.Any(a => a is SensorAction.ShowOverlay or SensorAction.HideOverlay)))
            .Select(r => new ConfigurationLink("tapo",r.Id,r.Name)));
        if (overlay) {
            links.AddRange(settings.Layouts.Where(l => l.Tiles.Any(t => t.CameraSlot == slots[1])).Select(l => new ConfigurationLink("layout",l.Id,l.Name)));
            links.AddRange(settings.AutomationViewLayouts.Where(l => l.Tiles.Any(t => t.CameraSlot == slots[1])).Select(l => new ConfigurationLink("automationLayout",l.Id,l.Name)));
        } else links.AddRange(settings.AllOverlays().Where(o => o.SourceCameraSlot == slot || o.HostCameraSlot == slot && !string.IsNullOrWhiteSpace(o.Camera.RtspUrl)).Select(o => new ConfigurationLink("overlay",o.Camera.Slot.ToString(),o.Camera.Name)));
        return links.ToArray();
    }
    public static ConfigurationLink[] Layouts(AutomationService? mqtt, TapoService tapo, IEnumerable<string> ids, bool automation, bool secondOnly = false)
    {
        var selected = ids.ToHashSet();
        var links = (mqtt?.CurrentSettings.Rules.Where(r => r.Action == AutomationAction.FocusedLayout && selected.Contains(r.LayoutId) && (!secondOnly || r.SecondCameraSlot != 0))
            .Select(r => new ConfigurationLink("mqtt",r.Id,r.Name)) ?? []).ToList();
        var action = automation ? SensorAction.AutomationLayout : SensorAction.Layout;
        links.AddRange(tapo.CurrentSettings.Rules.Where(r => (!secondOnly || r.SecondFocusCameraSlot != 0) && ((r.Action == action && selected.Contains(r.LayoutId)) || (r.ClearAction == action && selected.Contains(r.ClearLayoutId))))
            .Select(r => new ConfigurationLink("tapo",r.Id,r.Name)));
        return links.ToArray();
    }
}
