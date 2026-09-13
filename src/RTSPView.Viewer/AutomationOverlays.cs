using RTSPView.Core;

namespace RTSPView.Viewer;

public partial class MainWindow
{
    private readonly OverlayAutomationState _automationPresentation = new();
    private HashSet<int> _automatedSlots = [];
    private AutomationOverlayLease? _automatedFocus;
    private int? EffectiveFocusedSlot => _focusedSlot ?? (_automatedFocus?.Action == AutomationAction.FullScreen ? _automatedFocus.Slot : null);
    private WallLayout EffectiveLayout => !_focusedSlot.HasValue && _automatedFocus?.Action == AutomationAction.FocusedLayout
        ? AutomationConfiguration.FocusedLayout(_settings, _automatedFocus.Slot)
        : _settings.Layouts.Single(l => l.Id == _settings.ActiveLayoutId);

    private bool OverlayEnabled(DoorbellOverlaySettings overlay) => overlay.Camera.Enabled || _automatedSlots.Contains(overlay.Camera.Slot);

    private ViewerCommandResult ApplyAutomation(ViewerCommand command)
    {
        var presentation = command.Automation;
        if (presentation is null || presentation.Leases is null || presentation.Leases.Length > 1024 ||
            presentation.ConfigurationHash != AutomationConfiguration.Hash(_settings))
            return new(command.Id, false, "Viewer configuration changed; waiting for a fresh detection.");
        if (presentation.Leases.Any(l => l is null || !Enum.IsDefined(l.Action) || (l.Action == AutomationAction.Overlay
                ? !_settings.AllOverlays().Any(o => o.Camera.Slot == l.Slot && !string.IsNullOrWhiteSpace(o.Camera.RtspUrl))
                : !AutomationConfiguration.CanFocus(_settings, l.Action, l.Slot))))
            return new(command.Id, false, "Automation target is unavailable or unconfigured.");
        if (presentation.Leases.Length > 0 && (!CanDisplayOverlayWindows() || _focusedSlot.HasValue))
            return new(command.Id, false, "Manual focus or hidden viewer takes priority.");
        _automationPresentation.Update(presentation.Leases, DateTimeOffset.UtcNow);
        RefreshAutomationOverlays();
        return new(command.Id, true, "Automation applied.");
    }

    private void RefreshAutomationOverlays()
    {
        var active = _automationPresentation.ActiveSlots(DateTimeOffset.UtcNow);
        var focus = _focusedSlot.HasValue ? null : _automationPresentation.Focus(DateTimeOffset.UtcNow);
        var focusChanged = focus?.Action != _automatedFocus?.Action || focus?.Slot != _automatedFocus?.Slot;
        if (_automatedSlots.SetEquals(active) && !focusChanged) return;
        _automatedFocus = focus;
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
        _automationPresentation.Clear();
        RefreshAutomationOverlays();
    }
}
