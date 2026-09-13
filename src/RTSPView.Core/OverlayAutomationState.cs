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
    public HashSet<int> ActiveSlots(DateTimeOffset now) => _leases.Where(l => l.ExpiresAt > now && !_dismissed.Contains(l.Id)).Select(l => l.Slot).ToHashSet();
}
