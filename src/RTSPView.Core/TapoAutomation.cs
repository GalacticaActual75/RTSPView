namespace RTSPView.Core;

public enum ContactState { Unavailable, Closed, Open }
public enum SensorAction { Restore, ShowOverlay, HideOverlay, Layout, AutomationLayout }
public sealed record TapoHub(string Id, string Name, string Host);
public sealed record TapoSensor(string HubId, string DeviceId, string Name, string Model, ContactState State);
public sealed record TapoHubStatus(string Id, string Name, string Model, bool Connected, string Message);
public sealed record TapoSnapshot(TapoHubStatus[] Hubs, TapoSensor[] Sensors, TapoHub[]? DiscoveredHubs = null);
public sealed record TapoRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Door sensor";
    public bool Enabled { get; init; } = true;
    public string HubId { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public ContactState Match { get; init; } = ContactState.Open;
    public SensorAction Action { get; init; } = SensorAction.ShowOverlay;
    public SensorAction ClearAction { get; init; } = SensorAction.Restore;
    public SensorAction UnavailableAction { get; init; } = SensorAction.Restore;
    public int OverlaySlot { get; init; }
    public string LayoutId { get; init; } = "";
    public string ClearLayoutId { get; init; } = "";
    public int Priority { get; init; } = 50;
    public int FocusCameraSlot { get; init; }
    public int SecondFocusCameraSlot { get; init; }
}
public sealed record TapoSettings
{
    public bool Enabled { get; init; }
    public string Username { get; init; } = "";
    public int PollSeconds { get; init; } = 5;
    public TapoHub[] Hubs { get; init; } = [];
    public TapoRule[] Rules { get; init; } = [];

    public void Validate(AppSettings app, bool connect = false)
    {
        if (Username is null || Username.Length > 256 || ((Enabled || connect) && string.IsNullOrWhiteSpace(Username)))
            throw new InvalidDataException("Enter your Tapo account email.");
        if (PollSeconds is < 5 or > 60) throw new InvalidDataException("Check interval must be 5–60 seconds.");
        if (Hubs is null || Hubs.Length > 8 || ((Enabled || connect) && Hubs.Length == 0) || Hubs.Any(h => h is null))
            throw new InvalidDataException("Add 1–8 Tapo hubs.");
        if (Hubs.Select(h => h.Id).Distinct().Count() != Hubs.Length || Hubs.Select(h => h.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Hubs.Length)
            throw new InvalidDataException("Each hub needs a unique address and ID.");
        foreach (var hub in Hubs)
            if (!Guid.TryParse(hub.Id, out _) || string.IsNullOrWhiteSpace(hub.Name) || hub.Name.Length > 100 ||
                string.IsNullOrWhiteSpace(hub.Host) || hub.Host.Length > 253 ||
                Uri.CheckHostName(hub.Host) is UriHostNameType.Unknown || hub.Host.Contains(':'))
                throw new InvalidDataException("Enter a hub name and IPv4 address or hostname without a URL scheme or port.");
        if (Rules is null || Rules.Length > 32 || Rules.Any(r => r is null) || Rules.Select(r => r.Id).Distinct().Count() != Rules.Length)
            throw new InvalidDataException("Use at most 32 sensor rules with unique IDs.");
        foreach (var r in Rules)
        {
            if (!Guid.TryParse(r.Id, out _) || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 100 ||
                !Hubs.Any(h => h.Id == r.HubId) || string.IsNullOrWhiteSpace(r.DeviceId) || r.DeviceId.Length > 256 || r.DeviceId.Any(char.IsControl))
                throw new InvalidDataException("Each rule needs a name and a sensor from a configured hub.");
            if (r.Match is not (ContactState.Open or ContactState.Closed) || !Enum.IsDefined(r.Action) || !Enum.IsDefined(r.ClearAction) ||
                r.UnavailableAction is not (SensorAction.Restore or SensorAction.HideOverlay) || r.Priority is < 1 or > 100)
                throw new InvalidDataException("Select valid sensor states, actions, and priority (1–100; 1 is highest).");
            if (new[] { r.Action, r.ClearAction, r.UnavailableAction }.Any(a => a is SensorAction.ShowOverlay or SensorAction.HideOverlay) &&
                !app.AllOverlays().Any(o => o.Camera.Slot == r.OverlaySlot && !string.IsNullOrWhiteSpace(o.Camera.RtspUrl)))
                throw new InvalidDataException("Select an overlay with a configured stream.");
            if ((r.Action == SensorAction.Layout && !app.Layouts.Any(l => l.Id == r.LayoutId)) ||
                (r.ClearAction == SensorAction.Layout && !app.Layouts.Any(l => l.Id == r.ClearLayoutId)))
                throw new InvalidDataException("Select an available saved wall layout.");
            if (r.Action == SensorAction.AutomationLayout || r.ClearAction == SensorAction.AutomationLayout)
            {
                foreach (var id in new[] { r.Action == SensorAction.AutomationLayout ? r.LayoutId : null, r.ClearAction == SensorAction.AutomationLayout ? r.ClearLayoutId : null }.Where(id => id is not null))
                    if (!app.AutomationViewLayouts.Any(l => l.Id == id)) throw new InvalidDataException("Select an available automation layout.");
                if (!AutomationConfiguration.CanFocus(app, AutomationAction.FocusedLayout, r.FocusCameraSlot) ||
                    (r.SecondFocusCameraSlot != 0 && (r.SecondFocusCameraSlot == r.FocusCameraSlot || !AutomationConfiguration.CanFocus(app, AutomationAction.FocusedLayout, r.SecondFocusCameraSlot))))
                    throw new InvalidDataException("Select configured, distinct focus cameras.");
                if (r.SecondFocusCameraSlot != 0 && !app.AutomationViewLayouts.Any(l => l.FocusSlots.Length == 2 &&
                    ((r.Action == SensorAction.AutomationLayout && l.Id == r.LayoutId) || (r.ClearAction == SensorAction.AutomationLayout && l.Id == r.ClearLayoutId))))
                    throw new InvalidDataException("Select a two-focus automation layout for the second camera.");
            }
        }
    }
}

public sealed record SensorEffect(string RuleId, SensorAction Action, int OverlaySlot, string LayoutId, int Priority, int FocusCameraSlot = 0, int SecondFocusCameraSlot = 0);
public sealed record SensorPresentation(string ConfigurationHash, DateTimeOffset ExpiresAt, SensorEffect[] Effects);

public static class TapoRuleEngine
{
    // Stateless evaluation: startup and reconnect use the actual state, never a replayed edge.
    public static SensorEffect[] Evaluate(TapoSettings settings, IEnumerable<TapoSensor> sensors)
    {
        if (!settings.Enabled) return [];
        var readings = sensors.ToDictionary(s => (s.HubId, s.DeviceId));
        return settings.Rules.Where(r => r.Enabled).Select(r =>
        {
            var state = readings.TryGetValue((r.HubId, r.DeviceId), out var s) ? s.State : ContactState.Unavailable;
            var action = state == ContactState.Unavailable ? r.UnavailableAction : state == r.Match ? r.Action : r.ClearAction;
            return new SensorEffect(r.Id, action, r.OverlaySlot, state == r.Match ? r.LayoutId : r.ClearLayoutId, r.Priority, r.FocusCameraSlot, r.SecondFocusCameraSlot);
        }).Where(e => e.Action != SensorAction.Restore).OrderBy(e => e.Priority).ThenBy(e => e.RuleId, StringComparer.Ordinal).ToArray();
    }
}

public sealed class SensorPresentationState
{
    private SensorPresentation? _presentation;
    public void Update(SensorPresentation presentation) => _presentation = presentation;
    public void Clear() => _presentation = null;
    public IEnumerable<SensorEffect> Active(DateTimeOffset now) => _presentation is { } p && p.ExpiresAt > now
        ? p.Effects.OrderBy(e => e.Priority).ThenBy(e => e.RuleId, StringComparer.Ordinal) : [];
    public bool? Overlay(int slot, DateTimeOffset now) => Active(now).FirstOrDefault(e => e.OverlaySlot == slot && e.Action is SensorAction.ShowOverlay or SensorAction.HideOverlay)?.Action switch
    { SensorAction.ShowOverlay => true, SensorAction.HideOverlay => false, _ => null };
    public string? Layout(DateTimeOffset now) => Active(now).FirstOrDefault(e => e.Action == SensorAction.Layout)?.LayoutId;
    public string? WinningLayout(AutomationOverlayLease? detection, DateTimeOffset now)
    {
        var sensor = Active(now).FirstOrDefault(e => e.Action == SensorAction.Layout);
        return sensor is not null && (detection is null || sensor.Priority <= detection.Priority) ? sensor.LayoutId : null;
    }
    public SensorEffect? WinningView(AutomationOverlayLease? detection, DateTimeOffset now)
    {
        var sensor = Active(now).FirstOrDefault(e => e.Action is SensorAction.Layout or SensorAction.AutomationLayout);
        return sensor is not null && (detection is null || sensor.Priority <= detection.Priority) ? sensor : null;
    }
    public bool? WinningOverlay(int slot, int? detectionPriority, DateTimeOffset now)
    {
        var sensor = Active(now).FirstOrDefault(e => e.OverlaySlot == slot && e.Action is SensorAction.ShowOverlay or SensorAction.HideOverlay);
        return sensor is not null && (detectionPriority is null || sensor.Priority <= detectionPriority)
            ? sensor.Action == SensorAction.ShowOverlay : null;
    }
}
