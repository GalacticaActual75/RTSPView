using RTSPView.Core;

internal static class StreamCatalogChecks
{
    public static void Run()
    {
        var settings = new AppSettings();
        var original = settings.Cameras[1];
        var deleted = StreamCatalog.DeleteCamera(settings, 1).Normalize();
        if (StreamCatalog.LayoutCameras(deleted).Any(c => c.Slot == 1) || deleted.Layouts.Any(l => l.Tiles.Any(t => t.CameraSlot == 1)) || deleted.Cameras[1] != original)
            throw new Exception("Deletion failed to remove stream or renumbered a neighbor");
        if (!deleted.Normalize().DeletedCameraSlots.Contains(1)) throw new Exception("Deletion did not survive normalization");
        var overlay = settings.DoorbellOverlay with { ZoomPercent = 250, ViewportOpacityPercent = 30,
            Camera = settings.DoorbellOverlay.Camera with { RtspUrl = "rtsp://example.test/source", Enabled = false } };
        settings = settings with { DoorbellOverlay = overlay };
        var raw = StreamCatalog.LayoutCameras(settings).Single(c => c.Slot == 33);
        if (!raw.Enabled || raw.RtspUrl != overlay.Camera.RtspUrl || raw.Name == overlay.Camera.Name) throw new Exception("Raw source missing or conflated with shaped overlay");
        WallLayout.Validate([new() { Tiles = [new() { CameraSlot = 33 }] }], "default");
        WallLayout.Validate([new() { Tiles = [] }], "default");
        var resolved = AutomationLayouts.Resolve(settings, settings.AutomationViewLayouts[0], [33]);
        if (resolved.Tiles[0].CameraSlot != 33 || !AutomationConfiguration.CanFocus(settings, AutomationAction.FocusedLayout, 33)) throw new Exception("Raw overlay source cannot fill focus tile");
        try { StreamCatalog.DeleteCamera(settings, overlay.HostCameraSlot); throw new Exception("Dependent overlay host deleted"); } catch (InvalidDataException) { }
        var removedOverlay = StreamCatalog.DeleteOverlay(settings, 10).Normalize();
        if (removedOverlay.DoorbellOverlay.Camera.Enabled || removedOverlay.DoorbellOverlay.Camera.RtspUrl.Length != 0 ||
            StreamCatalog.LayoutCameras(removedOverlay).Any(c => c.Slot == 33) || !removedOverlay.DeletedOverlaySlots.Contains(10))
            throw new Exception("Deleted overlay remained configured or visible in stream catalog");
        if (removedOverlay.GarageOverlay != settings.Normalize().GarageOverlay) throw new Exception("Overlay deletion changed a neighbor");
        var resurrect = (removedOverlay with { DoorbellOverlay = overlay }).Normalize();
        if (resurrect.DoorbellOverlay.Camera.RtspUrl.Length != 0) throw new Exception("Stale overlay settings resurrected deleted source");
        var inLayout = settings with { Layouts = [new() { Tiles = [new() { CameraSlot = 33 }] }] };
        try { StreamCatalog.DeleteOverlay(inLayout, 10); throw new Exception("Layout dependency ignored"); } catch (InvalidDataException) { }
        try { StreamCatalog.DeleteOverlay(removedOverlay, 10); throw new Exception("Repeated delete allowed"); } catch (InvalidDataException) { }
        var additional = (settings with { AdditionalOverlays = [new() { Camera = new() { RtspUrl = "rtsp://example.test/a" } }, new() { Camera = new() { RtspUrl = "rtsp://example.test/b" } }] }).Normalize();
        var removedAdditional = StreamCatalog.DeleteOverlay(additional, 12);
        if (removedAdditional.AdditionalOverlays[1] != additional.AdditionalOverlays[1] || removedAdditional.AdditionalOverlays[0].Camera.RtspUrl.Length != 0)
            throw new Exception("Additional overlay deletion renumbered or retained a source");
        Console.WriteLine("PASS stable stream deletion, empty layouts, raw overlay catalog, focus and host dependency");
    }
}
