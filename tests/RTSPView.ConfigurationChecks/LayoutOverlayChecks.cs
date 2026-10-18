using RTSPView.Core;
using RTSPView.Infrastructure;

internal static class LayoutOverlayChecks
{
    public static async Task Run(string root)
    {
        var settings = new AppSettings();
        var overlay = settings.GarageOverlay;
        var placement = new LayoutOverlayPlacement { OverlaySlot = 11, HorizontalPositionPercent = 80, VerticalPositionPercent = 25 };
        var standard = settings.Layouts[0] with { OverlayPlacements = [placement] };
        var automation = settings.AutomationViewLayouts[0] with { OverlayPlacements = [placement with { Hidden = true }] };
        var store = new JsonSettingsStore(Path.Combine(root, "layout-overlays.json"));
        await store.SaveAsync(settings with { Layouts = [standard], AutomationViewLayouts = [automation] });
        var saved = await store.LoadAsync();
        Check(saved.Layouts[0].ApplyOverlayPlacement(overlay).ViewportHorizontalPositionPercent == 80, "Standard overlay position did not persist");
        Check(saved.Layouts[0].ApplyOverlayPlacement(overlay).ViewportVerticalPositionPercent == 25, "Vertical position did not persist");
        Check(saved.GarageOverlay == overlay && settings.Layouts[0].ApplyOverlayPlacement(overlay) == overlay, "Layout edit changed shared or other layout settings");
        Check(saved.AutomationViewLayouts[0].IsOverlayHidden(11) && !saved.Layouts[0].IsOverlayHidden(11), "Removal leaked across layouts");
        var resolved = AutomationLayouts.Resolve(saved, saved.AutomationViewLayouts[0], [3]);
        Check(resolved.IsOverlayHidden(11) && resolved.ApplyOverlayPlacement(overlay).ViewportHorizontalPositionPercent == 80, "Focus resolution discarded overlay placement");
        var restored = automation with { OverlayPlacements = [placement with { HorizontalPositionPercent = null, VerticalPositionPercent = null }] };
        Check(!restored.IsOverlayHidden(11) && restored.ApplyOverlayPlacement(overlay) == overlay, "Restore/shared placement did not restore defaults");
        foreach (var invalid in new IReadOnlyList<LayoutOverlayPlacement>[] {
            [placement, placement], [placement with { OverlaySlot = 3 }], [placement with { HorizontalPositionPercent = -1 }],
            [placement with { VerticalPositionPercent = 101 }], [null!], null! })
        {
            try { WallLayout.Validate([standard with { OverlayPlacements = invalid }], standard.Id);throw new Exception("Invalid overlay placement accepted"); }
            catch (InvalidDataException) { }
        }
        var deleted = StreamCatalog.DeleteOverlay(saved, 11);
        Check(deleted.Layouts.Concat(deleted.AutomationViewLayouts).All(l => l.OverlayPlacements.Count == 0), "Deleted overlay retained stale placements for reused slot");
        Console.WriteLine("PASS layout-only overlay position/removal, persisted settings, focus resolution, shared fallback, validation and deletion cleanup.");
    }
    private static void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
}
