using RTSPView.Core;

namespace RTSPView.Viewer;

public partial class MainWindow
{
    private readonly OverlayAutomationState _automationPresentation = new();
    private HashSet<int> _automatedSlots = [];

    private bool OverlayEnabled(DoorbellOverlaySettings overlay) => overlay.Camera.Enabled || _automatedSlots.Contains(overlay.Camera.Slot);

    private ViewerCommandResult ApplyAutomation(ViewerCommand command)
    {
        var presentation = command.Automation;
        if (presentation is null || presentation.Leases is null || presentation.Leases.Length > 32 ||
            presentation.ConfigurationHash != AutomationConfiguration.Hash(_settings))
            return new(command.Id, false, "Viewer configuration changed; waiting for a fresh detection.");
        if (presentation.Leases.Any(l => l is null || !_settings.AllOverlays().Any(o => o.Camera.Slot == l.Slot && !string.IsNullOrWhiteSpace(o.Camera.RtspUrl))))
            return new(command.Id, false, "Overlay is unavailable or unconfigured.");
        if (presentation.Leases.Length > 0 && (!CanDisplayOverlayWindows() || _focusedSlot.HasValue))
            return new(command.Id, false, "Manual focus or hidden viewer takes priority.");
        _automationPresentation.Update(presentation.Leases, DateTimeOffset.UtcNow);
        RefreshAutomationOverlays();
        return new(command.Id, true, "Overlay automation applied.");
    }

    private void RefreshAutomationOverlays()
    {
        var active = _automationPresentation.ActiveSlots(DateTimeOffset.UtcNow);
        if (_automatedSlots.SetEquals(active)) return;
        var changed = _automatedSlots.Union(active).Where(s => _automatedSlots.Contains(s) != active.Contains(s)).ToArray();
        _automatedSlots = active;
        foreach (var slot in changed)
        {
            var camera = _settings.AllOverlays().FirstOrDefault(o => o.Camera.Slot == slot)?.Camera;
            var tile = _allTiles.FirstOrDefault(t => t.Slot == slot);
            if (camera is not null && tile is not null && !camera.Enabled)
                tile.Apply(camera with { Enabled = active.Contains(slot) });
        }
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
