namespace RTSPView.Core;

public static class AutomationLayouts
{
    public static IReadOnlyList<WallLayout> Defaults() => [Create(1), Create(2)];
    private static WallLayout Create(int count)
    {
        var columns = count == 1 ? 3 : 4;
        var tiles = new List<WallTile>();
        for (var i = 0; i < count; i++) tiles.Add(new() { CameraSlot = i + 1, Column = i * 2, RowSpan = 2, ColumnSpan = 2 });
        var next = count;
        for (var row = 0; row < 4 && next < 9; row++)
            for (var col = 0; col < columns && next < 9; col++)
                if (row >= 2 || col >= count * 2) tiles.Add(new() { CameraSlot = AppSettings.MainCameraSlots[next++], Row = row, Column = col });
        return new() { Id = "focus-" + count, Name = count == 1 ? "One large camera" : "Two large cameras",
            Rows = 4, Columns = columns, Tiles = tiles, FocusSlots = Enumerable.Range(1, count).ToArray() };
    }
    public static void Validate(IReadOnlyList<WallLayout>? layouts)
    {
        WallLayout.Validate(layouts, layouts?.FirstOrDefault()?.Id, 6);
        foreach (var layout in layouts!)
            if (layout.FocusSlots is null || layout.FocusSlots.Length is < 1 or > 2 || layout.FocusSlots.Distinct().Count() != layout.FocusSlots.Length ||
                layout.FocusSlots.Any(slot => !layout.Tiles.Any(t => t.CameraSlot == slot)))
                throw new InvalidDataException("Automation layouts require one or two distinct focus positions assigned to tiles.");
    }
    public static WallLayout Resolve(AppSettings settings, WallLayout template, IEnumerable<int> activeSlots)
    {
        var configured = settings.Cameras.Take(settings.CameraCount).Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.RtspUrl)).Select(c => c.Slot).ToHashSet();
        var targets = activeSlots.Where(configured.Contains).Distinct().Take(template.FocusSlots.Length).ToArray();
        var tiles = template.Tiles.ToArray();
        var positions = template.FocusSlots.Select(slot => Array.FindIndex(tiles, t => t.CameraSlot == slot)).ToArray();
        for (var i = 0; i < targets.Length; i++)
        {
            var position = positions[i];
            var other = Array.FindIndex(tiles, t => t.CameraSlot == targets[i]);
            var displaced = tiles[position].CameraSlot;
            tiles[position] = tiles[position] with { CameraSlot = targets[i] };
            if (other >= 0 && other != position) tiles[other] = tiles[other] with { CameraSlot = displaced };
        }
        return template with { Tiles = tiles.Where(t => configured.Contains(t.CameraSlot)).ToArray() };
    }
}
