namespace RTSPView.Core;

public sealed record AutomationRuleVisibility(string Source, string RuleId, bool Effective, string Reason);
public sealed record AutomationVisibility(DateTimeOffset At, AutomationRuleVisibility[] Rules)
{
    public static AutomationVisibility Capture(OverlayAutomationState mqtt, SensorPresentationState tapo,
        AppSettings settings, DateTimeOffset now, bool manualFocus, bool visible)
    {
        var leases = mqtt.Pending(now);
        var focus = mqtt.Focus(now);
        var sensorView = tapo.WinningView(focus, now);
        var fullScreen = sensorView is null && focus?.Action == AutomationAction.FullScreen;
        var sensors = tapo.Active(now).ToArray();
        var result = new List<AutomationRuleVisibility>();
        var focusCount = settings.AutomationViewLayouts.FirstOrDefault(l => l.Id == focus?.LayoutId)?.FocusSlots.Length ?? 1;
        var contributors = new HashSet<string>();
        if (focus?.Action == AutomationAction.FocusedLayout)
        {
            var candidates = leases.Where(l => !mqtt.IsDismissed(l.Id) && l.Action == focus.Action && l.LayoutId == focus.LayoutId && l.Priority == focus.Priority);
            var ordered = focus.AllowNewerDetection ? candidates.OrderByDescending(l => l.StartedAt).ThenBy(l => l.Id, StringComparer.Ordinal) : candidates.OrderBy(l => l.StartedAt).ThenBy(l => l.Id, StringComparer.Ordinal);
            var usedSlots = new HashSet<int>();
            foreach (var candidate in ordered.OrderBy(l => l.Id == focus.Id ? 0 : 1))
                foreach (var slot in candidate.SecondCameraSlot > 0 ? new[] { candidate.Slot, candidate.SecondCameraSlot } : new[] { candidate.Slot })
                    if (usedSlots.Count < focusCount && usedSlots.Add(slot)) contributors.Add(candidate.Id);
        }
        foreach (var lease in leases)
        {
            var reason = !visible ? "Viewer hidden" : manualFocus ? "Manual focus" : mqtt.IsDismissed(lease.Id) ? "Manually dismissed" : "";
            if (reason.Length == 0)
            {
                if (lease.Action == AutomationAction.Overlay)
                {
                    var winning = leases.Where(l => !mqtt.IsDismissed(l.Id) && l.Action == AutomationAction.Overlay && l.Slot == lease.Slot).OrderBy(l => l.Priority).ThenBy(l => l.Id, StringComparer.Ordinal).First();
                    if (winning.Id != lease.Id || tapo.WinningOverlay(lease.Slot, winning.Priority, now).HasValue) reason = "Another rule wins overlay priority";
                    if (fullScreen) reason = "Fullscreen view hides overlays";
                }
                else if (sensorView is not null) reason = "Tapo rule wins view priority";
                else if (focus?.Id != lease.Id && !contributors.Contains(lease.Id)) reason = "Another rule wins view priority";
            }
            result.Add(new("MQTT", lease.RuleId, reason.Length == 0, reason.Length == 0 ? "Effective" : reason));
        }
        foreach (var effect in sensors)
        {
            var reason = !visible ? "Viewer hidden" : manualFocus ? "Manual focus" : "";
            if (reason.Length == 0)
            {
                if (effect.Action is SensorAction.Layout or SensorAction.AutomationLayout)
                { if (sensorView?.RuleId != effect.RuleId) reason = "Another rule wins view priority"; }
                else
                {
                    var winner = sensors.First(e => e.OverlaySlot == effect.OverlaySlot && e.Action is SensorAction.ShowOverlay or SensorAction.HideOverlay);
                    if (winner.RuleId != effect.RuleId || !tapo.WinningOverlay(effect.OverlaySlot, mqtt.OverlayPriority(effect.OverlaySlot, now), now).HasValue) reason = "Another rule wins overlay priority";
                    if (fullScreen) reason = "Fullscreen view hides overlays";
                }
            }
            result.Add(new("Tapo", effect.RuleId, reason.Length == 0, reason.Length == 0 ? "Effective" : reason));
        }
        return new(now, result.ToArray());
    }
}
