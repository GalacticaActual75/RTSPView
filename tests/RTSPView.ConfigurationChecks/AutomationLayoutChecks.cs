using RTSPView.Core;

internal static class AutomationLayoutChecks
{
    public static void Run()
    {
        var settings = new AppSettings();
        settings = settings with { Cameras = settings.Cameras.Select(c => c with { Enabled = true, RtspUrl = "rtsp://example.test/" + c.Slot }).ToArray() };
        AutomationLayouts.Validate(settings.AutomationViewLayouts);
        var template = settings.AutomationViewLayouts[1];
        Check(template.Tiles[0].CameraSlot == -1 && template.Tiles[1].CameraSlot == -2, "Focus tiles must not bind cameras");
        var legacy = template with { FocusSlots = [1,2], Tiles = template.Tiles.Select(t => t.CameraSlot < 0 ? t with { CameraSlot = -t.CameraSlot } : t).ToArray() };
        var migrated = AutomationLayouts.Normalize([legacy])[0];
        Check(migrated.FocusSlots.SequenceEqual(new[]{-1,-2}) && migrated.Tiles[0].CameraSlot == -1, "Legacy focus assignments did not migrate");
        var hash = AutomationConfiguration.Hash(settings);
        foreach (var first in Enumerable.Range(1,9)) foreach (var second in Enumerable.Range(1,9).Where(s=>s!=first))
        {
            var result = AutomationLayouts.Resolve(settings, template, [first, second]);
            Check(result.Tiles[0].CameraSlot == first && result.Tiles[1].CameraSlot == second, "Two focus cameras did not occupy selected positions");
            Check(result.Tiles.Where(t=>t.CameraSlot>0).Select(t=>t.CameraSlot).Distinct().Count()==result.Tiles.Count(t=>t.CameraSlot>0), "Two focus positions duplicated or lost a camera");
            Check(result.Tiles[0].RowSpan==2 && result.Tiles[1].ColumnSpan==2, "Focus geometry changed");
        }
        var one = AutomationLayouts.Resolve(settings, template, [5]);
        Check(one.Tiles[0].CameraSlot==5 && one.Tiles[1].CameraSlot==0, "Unused focus tile must be empty");
        Check(AutomationConfiguration.Hash(settings)==hash, "Automation modified saved layouts");
        try { AutomationLayouts.Validate([template with { FocusSlots=[1,1] }]); throw new Exception("Duplicate focus position accepted"); } catch(InvalidDataException){}
        try { AutomationLayouts.Validate([template with { FocusSlots=[32] }]); throw new Exception("Missing focus position accepted"); } catch(InvalidDataException){}
        var now=DateTimeOffset.UtcNow;
        var state=new OverlayAutomationState();
        state.Update([new("one",5,now.AddSeconds(10),AutomationAction.FocusedLayout,now,"r1",template.Id),
            new("two",6,now.AddSeconds(20),AutomationAction.FocusedLayout,now.AddSeconds(1),"r2",template.Id),
            new("other",7,now.AddSeconds(20),AutomationAction.FocusedLayout,now.AddSeconds(2),"r3","different")],now);
        Check(state.FocusSlots(now,template.Id).SequenceEqual(new[]{5,6}), "Active focus ordering or layout isolation failed");
        Check(state.FocusSlots(now.AddSeconds(11),template.Id).SequenceEqual(new[]{6}), "Expired focus did not release position");
        state.Dismiss();Check(state.FocusSlots(now,template.Id).Length==0,"Manual dismissal left a focus position active");
        Console.WriteLine("PASS saved automation layouts, dynamic focus tiles, empty second position, isolation, expiry, validation and no mutation");
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
