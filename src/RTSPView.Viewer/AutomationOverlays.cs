using RTSPView.Core;

namespace RTSPView.Viewer;

public partial class MainWindow
{
    private readonly OverlayAutomationState _automationPresentation = new();
    private HashSet<int> _automatedSlots = [];
    private AutomationOverlayLease? _automatedFocus;
    private string _focusLayoutSignature = "";
    private readonly SensorPresentationState _sensorPresentation = new();
    private string? _sensorLayout;
    private Dictionary<int, bool> _sensorOverlays = [];
    private int? EffectiveFocusedSlot => _focusedSlot ?? (_sensorLayout is null && _automatedFocus?.Action == AutomationAction.FullScreen ? _automatedFocus.Slot : null);
    private WallLayout EffectiveLayout => !_focusedSlot.HasValue && _sensorLayout is not null
        ? _settings.Layouts.FirstOrDefault(l => l.Id == _sensorLayout) ?? _settings.Layouts.Single(l => l.Id == _settings.ActiveLayoutId)
        : !_focusedSlot.HasValue && _automatedFocus?.Action == AutomationAction.FocusedLayout
        ? ResolveAutomationLayout()
        : _settings.Layouts.Single(l => l.Id == _settings.ActiveLayoutId);

    private WallLayout ResolveAutomationLayout()
    {
        var template = _settings.AutomationViewLayouts.FirstOrDefault(l => l.Id == _automatedFocus!.LayoutId);
        return template is null ? AutomationConfiguration.FocusedLayout(_settings, _automatedFocus!.Slot)
            : AutomationLayouts.Resolve(_settings, template, _automationPresentation.FocusSlots(DateTimeOffset.UtcNow, template.Id));
    }

    private bool OverlayEnabled(DoorbellOverlaySettings overlay) => _sensorOverlays.TryGetValue(overlay.Camera.Slot, out var enabled)
        ? enabled : overlay.Camera.Enabled || _automatedSlots.Contains(overlay.Camera.Slot);

    private ViewerCommandResult ApplySensorAutomation(ViewerCommand command)
    {
        var p = command.Sensors;
        var now = DateTimeOffset.UtcNow;
        if (p is null || p.Effects is null || p.Effects.Length > 32 || p.ConfigurationHash != AutomationConfiguration.Hash(_settings) ||
            p.ExpiresAt <= now || p.ExpiresAt > now.AddSeconds(30) || p.Effects.Any(e => e is null ||
                !Enum.IsDefined(e.Action) || e.Priority is < 1 or > 100 ||
                (e.Action == SensorAction.Layout ? !_settings.Layouts.Any(l => l.Id == e.LayoutId) :
                 e.Action != SensorAction.Restore && !_settings.AllOverlays().Any(o => o.Camera.Slot == e.OverlaySlot && !string.IsNullOrWhiteSpace(o.Camera.RtspUrl)))))
            return new(command.Id, false, "Sensor target or configuration is unavailable.");
        _sensorPresentation.Update(p);
        RefreshAutomationOverlays();
        return new(command.Id, true, _focusedSlot.HasValue ? "Sensor rule applied; manual focus takes priority." : "Sensor rule applied.");
    }

    private ViewerCommandResult ApplyAutomation(ViewerCommand command)
    {
        var presentation = command.Automation;
        if (presentation is null || presentation.Leases is null || presentation.Leases.Length > 1024 ||
            presentation.ConfigurationHash != AutomationConfiguration.Hash(_settings))
            return new(command.Id, false, "Viewer configuration changed; waiting for a fresh detection.");
        if (presentation.Leases.Any(l => l is null || l.Priority is < 1 or > 100 || !Enum.IsDefined(l.Action) || (l.Action == AutomationAction.Overlay
                ? !_settings.AllOverlays().Any(o => o.Camera.Slot == l.Slot && !string.IsNullOrWhiteSpace(o.Camera.RtspUrl))
                : !AutomationConfiguration.CanFocus(_settings, l.Action, l.Slot))))
            return new(command.Id, false, "Automation target is unavailable or unconfigured.");
        if (presentation.Leases.Any(l => l.LayoutId is null || (l.Action == AutomationAction.FocusedLayout && l.LayoutId.Length > 0 && !_settings.AutomationViewLayouts.Any(t => t.Id == l.LayoutId))))
            return new(command.Id, false, "Automation layout is unavailable.");
        if (presentation.Leases.Any(l => l.SecondCameraSlot != 0 && (l.Action != AutomationAction.FocusedLayout ||
            !AutomationConfiguration.CanFocus(_settings, AutomationAction.FocusedLayout, l.SecondCameraSlot) ||
            !_settings.AutomationViewLayouts.Any(t => t.Id == l.LayoutId && t.FocusSlots.Length == 2))))
            return new(command.Id, false, "Second focus camera or layout is unavailable.");
        if (presentation.Leases.Length > 0 && (!CanDisplayOverlayWindows() || _focusedSlot.HasValue))
            return new(command.Id, false, "Manual focus or hidden viewer takes priority.");
        _automationPresentation.Update(presentation.Leases, DateTimeOffset.UtcNow);
        RefreshAutomationOverlays();
        return new(command.Id, true, "Automation applied.");
    }

    private void RefreshAutomationOverlays()
    {
        var now = DateTimeOffset.UtcNow;
        var focus = _focusedSlot.HasValue ? null : _automationPresentation.Focus(now);
        var sensorLayout = _sensorPresentation.WinningLayout(focus, now);
        var sensorOverlays = _settings.AllOverlays().Select(o => (Slot: o.Camera.Slot,
                Enabled: _sensorPresentation.WinningOverlay(o.Camera.Slot, _automationPresentation.OverlayPriority(o.Camera.Slot, now), now)))
            .Where(o => o.Enabled.HasValue).ToDictionary(o => o.Slot, o => o.Enabled!.Value);
        var sensorChanged = _sensorOverlays.Count != sensorOverlays.Count || _sensorOverlays.Any(p => !sensorOverlays.TryGetValue(p.Key, out var v) || v != p.Value);
        var sensorLayoutChanged = sensorLayout != _sensorLayout;
        _sensorLayout = sensorLayout; _sensorOverlays = sensorOverlays;
        var active = _automationPresentation.ActiveSlots(DateTimeOffset.UtcNow);
        var signature = focus?.Action == AutomationAction.FocusedLayout
            ? focus.LayoutId + ":" + string.Join(",", _automationPresentation.FocusSlots(DateTimeOffset.UtcNow, focus.LayoutId)) : "";
        var focusChanged = sensorLayoutChanged || focus?.Action != _automatedFocus?.Action || focus?.Slot != _automatedFocus?.Slot || signature != _focusLayoutSignature;
        if (_automatedSlots.SetEquals(active) && !focusChanged && !sensorChanged) return;
        _automatedFocus = focus;
        _focusLayoutSignature = signature;
        _automatedSlots = active;
        if (focusChanged && _tiles.Length > 0) ApplyWallLayout();
        // Composited overlay feeds remain warm. Detection, expiry and dismissal
        // change window visibility without starting or stopping their players.
        QueueOverlayLayouts();
    }

    private void DismissAutomation()
    {
        _automationPresentation.Dismiss();
        RefreshAutomationOverlays();
    }

    private void ClearAutomation()
    {
        _sensorPresentation.Clear();
        _automationPresentation.Clear();
        RefreshAutomationOverlays();
    }
}
