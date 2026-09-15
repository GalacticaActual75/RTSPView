namespace RTSPView.Core;

// Viewer-local leases expire even when Controller stops. Manual dismissal lasts
// through the current detection episode, including subsequent renewals.
public sealed class OverlayAutomationState
{
    private AutomationOverlayLease[] _leases = [];
    private readonly HashSet<string> _dismissed = [];
    public void Update(AutomationOverlayLease[] leases, DateTimeOffset now)
    {
        _leases = leases.Where(l => l.ExpiresAt > now && l.ExpiresAt <= now.AddMinutes(120)).ToArray();
        _dismissed.IntersectWith(_leases.Select(l => l.Id));
    }
    public void Dismiss() { foreach (var lease in _leases) _dismissed.Add(lease.Id); }
    public void Clear() { _leases = []; _dismissed.Clear(); }
    private IEnumerable<AutomationOverlayLease> Active(DateTimeOffset now) => _leases.Where(l => l.ExpiresAt > now && !_dismissed.Contains(l.Id));
    public HashSet<int> ActiveSlots(DateTimeOffset now) => Active(now).Where(l => l.Action == AutomationAction.Overlay).Select(l => l.Slot).ToHashSet();
    public AutomationOverlayLease? Focus(DateTimeOffset now) => Active(now).Where(l => l.Action != AutomationAction.Overlay)
        .OrderBy(l => l.Action == AutomationAction.FullScreen ? 0 : 1).ThenBy(l => l.StartedAt).ThenBy(l => l.Id, StringComparer.Ordinal).FirstOrDefault();
    public int[] FocusSlots(DateTimeOffset now, string layoutId) => Active(now)
        .Where(l => l.Action == AutomationAction.FocusedLayout && l.LayoutId == layoutId)
        .OrderBy(l => l.StartedAt).ThenBy(l => l.Id, StringComparer.Ordinal).SelectMany(l => l.SecondCameraSlot > 0 ? new[] { l.Slot, l.SecondCameraSlot } : new[] { l.Slot }).Distinct().Take(2).ToArray();
}
