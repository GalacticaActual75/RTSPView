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
        double Area(double[] rows, double[] columns) => layout.Tiles.Sum(t => Math.Pow(Math.Min(rows.Skip(t.Row).Take(t.RowSpan).Sum(), columns.Skip(t.Column).Take(t.ColumnSpan).Sum()), 2));
        if (Area(tracks.Rows, tracks.Columns) < Area([.25,.25,.25,.25], [1d/3,1d/3,1d/3]) + .07) throw new Exception("Focus layout did not meaningfully reduce unused picture area");
        // Same deterministic fixture is checked by the browser geometry test.
        if (Math.Abs(tracks.Rows[3] - .3) > 1e-10 || Math.Abs(tracks.Columns[2] - .2653333333333333) > 1e-10) throw new Exception("Browser/native geometry diverged");
        foreach (var item in new[] {layout, layout with {AspectRatio="9:16"}}.Concat(AutomationLayouts.Defaults()))
        {
            var result=WallProportions.Calculate(item);
            foreach(var axis in new[]{result.Rows,result.Columns})
                if(Math.Abs(axis.Sum()-1)>1e-10 || axis.Any(v=>!double.IsFinite(v)||v<=0)) throw new Exception("Invalid track bounds");
        }
        var normalized = (new AppSettings { DiagnosticsAutoOpenExcludedSlots = [1,17,17,-1,33] }).Normalize();
        if (!normalized.DiagnosticsAutoOpenExcludedSlots.SequenceEqual(new[]{1,17})) throw new Exception("Invalid diagnostics exclusions normalization");
        Console.WriteLine("PASS balanced tile proportions, unchanged 3x3, browser parity and diagnostics exclusions");
    }
}
