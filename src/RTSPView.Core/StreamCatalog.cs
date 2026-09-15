namespace RTSPView.Core;

public static class StreamCatalog
{
    public const int MaximumSlot = 48;
    public static bool IsOverlaySource(int slot) => slot is >= 33 and <= 48;
    public static int SourceSlot(int overlaySlot) => overlaySlot + 23;
    public static IEnumerable<CameraSettings> LayoutCameras(AppSettings settings) =>
        settings.Cameras.Take(settings.CameraCount).Where(c => !settings.DeletedCameraSlots.Contains(c.Slot))
            .Concat(settings.AllOverlays().Where(o => !string.IsNullOrWhiteSpace(o.Camera.RtspUrl))
                .Select(o => o.Camera with { Slot = SourceSlot(o.Camera.Slot), Name = o.Camera.Name + " (overlay source)", Enabled = true }));

    public static AppSettings DeleteCamera(AppSettings settings, int slot)
    {
        if (!settings.Cameras.Take(settings.CameraCount).Any(c => c.Slot == slot) || settings.DeletedCameraSlots.Contains(slot))
            throw new InvalidDataException("That stream has already been removed or does not exist.");
        if (settings.AllOverlays().Any(o => o.HostCameraSlot == slot && !string.IsNullOrWhiteSpace(o.Camera.RtspUrl)))
            throw new InvalidDataException("An overlay uses this stream as its host. Choose another host in Overlays before deleting it.");
        return settings with {
            DeletedCameraSlots = [..settings.DeletedCameraSlots, slot],
            Camera = slot == 1 ? new CameraSettings() : settings.Camera,
            Cameras = settings.Cameras.Select(c => c.Slot == slot ? new CameraSettings { Slot = slot, Name = "Camera " + (Array.IndexOf(AppSettings.MainCameraSlots, slot) + 1), Enabled = false } : c).ToArray(),
            Layouts = settings.Layouts.Select(l => l with { Tiles = l.Tiles.Where(t => t.CameraSlot != slot).ToArray() }).ToArray(),
            AutomationViewLayouts = settings.AutomationViewLayouts.Select(l => l with { Tiles = l.Tiles.Where(t => t.CameraSlot != slot).ToArray() }).ToArray(),
            DiagnosticsAutoOpenExcludedSlots = settings.DiagnosticsAutoOpenExcludedSlots.Where(s => s != slot).ToArray()
        };
    }
}
