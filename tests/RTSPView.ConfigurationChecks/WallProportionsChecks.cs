using RTSPView.Core;

internal static class WallProportionsChecks
{
    public static void Run()
    {
        var standard = WallProportions.Calculate(new());
        if (standard.Rows.Concat(standard.Columns).Any(v => Math.Abs(v - 1d / 3) > 1e-12)) throw new Exception("3x3 proportions changed");
        var layout = new WallLayout { Rows = 4, Columns = 3, Tiles = [
            new() { CameraSlot = 1, RowSpan = 3, ColumnSpan = 2 },
            ..Enumerable.Range(0,4).Select(r => new WallTile { CameraSlot = r+2, Row = r, Column = 2 }),
            new() { CameraSlot = 6, Row = 3 }, new() { CameraSlot = 7, Row = 3, Column = 1 }] };
        var tracks = WallProportions.Calculate(layout);
        // Same deterministic fixture is checked by the browser geometry test.
        if (tracks.Rows.Any(v => Math.Abs(v - .25) > 1e-10) || tracks.Columns.Any(v => Math.Abs(v - 1d/3) > 1e-10)) throw new Exception("Small tile dimensions differ");
        var mixed = new WallLayout { Rows=4, Columns=4, Tiles=[new() {RowSpan=3,ColumnSpan=2},new() {Column=2,RowSpan=2,ColumnSpan=2},
            ..new[]{2,3}.Select(c=>new WallTile {Row=2,Column=c}), ..Enumerable.Range(0,4).Select(c=>new WallTile {Row=3,Column=c})] };
        var balanced = WallProportions.Calculate(mixed);
        if (Math.Abs(balanced.Rows[2]-.27)>1e-10 || Math.Abs(balanced.Rows[3]-.27)>1e-10 || balanced.Columns.Any(v=>Math.Abs(v-.25)>1e-10))
            throw new Exception("Tall/wide focus layout lost equal small tiles or browser parity");
        foreach (var item in new[] {layout, layout with {AspectRatio="9:16"}}.Concat(AutomationLayouts.Defaults()))
        {
            var result=WallProportions.Calculate(item);
            foreach(var axis in new[]{result.Rows,result.Columns})
                if(Math.Abs(axis.Sum()-1)>1e-10 || axis.Any(v=>!double.IsFinite(v)||v<=0)) throw new Exception("Invalid track bounds");
        }
        var normalized = (new AppSettings { DiagnosticsAutoOpenExcludedSlots = [1,17,17,-1,49] }).Normalize();
        if (!normalized.DiagnosticsAutoOpenExcludedSlots.SequenceEqual(new[]{1,17})) throw new Exception("Invalid diagnostics exclusions normalization");
        Console.WriteLine("PASS balanced tile proportions, unchanged 3x3, browser parity and diagnostics exclusions");
    }
}
