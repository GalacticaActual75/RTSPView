namespace RTSPView.Core;

// Balance shared grid tracks toward 16:9 camera pictures. Grid placement and
// spans stay intact; video rendering remains contain/fit and never crops.
public static class WallProportions
{
    public static (double[] Rows, double[] Columns) Calculate(WallLayout layout)
    {
        if (layout.RowWeights.Length == layout.Rows && layout.ColumnWeights.Length == layout.Columns)
            return (layout.RowWeights.ToArray(), layout.ColumnWeights.ToArray());
        var rows = Enumerable.Repeat(1d / layout.Rows, layout.Rows).ToArray();
        var columns = Enumerable.Repeat(1d / layout.Columns, layout.Columns).ToArray();
        var small = layout.Tiles.Where(t => t.RowSpan == 1 && t.ColumnSpan == 1).ToArray();
        int[][] Groups(int count, IEnumerable<int> linked)
        {
            var shared = linked.Distinct().Order().ToArray();
            return (shared.Length > 0 ? new[] { shared } : Array.Empty<int[]>())
                .Concat(Enumerable.Range(0, count).Where(i => !shared.Contains(i)).Select(i => new[] { i })).ToArray();
        }
        var rowGroups = Groups(layout.Rows, small.Select(t => t.Row));
        var columnGroups = Groups(layout.Columns, small.Select(t => t.Column));
        var aspect = (double)layout.EffectiveWidth / layout.EffectiveHeight;
        double Score()
        {
            var score = 0d;
            foreach (var tile in layout.Tiles)
            {
                var width = columns.Skip(tile.Column).Take(tile.ColumnSpan).Sum();
                var height = rows.Skip(tile.Row).Take(tile.RowSpan).Sum();
                var error = Math.Log(aspect * width / height / (16d / 9));
                score += tile.RowSpan * tile.ColumnSpan * error * error;
            }
            return score;
        }
        var best = Score();
        foreach (var step in new[] { .08, .025, .008, .002 })
            for (var pass = 0; pass < 12; pass++)
            {
                var improved = false;
                foreach (var (tracks, groups) in new[] { (rows, rowGroups), (columns, columnGroups) })
                    for (var a = 0; a < groups.Length; a++)
                        for (var b = 0; b < groups.Length; b++)
                        {
                            var add = step / groups[a].Length; var subtract = step / groups[b].Length;
                            if (a == b || groups[b].Any(i => tracks[i] - subtract < .35 / tracks.Length) || groups[a].Any(i => tracks[i] + add > 2d / tracks.Length)) continue;
                            foreach (var i in groups[a]) tracks[i] += add;
                            foreach (var i in groups[b]) tracks[i] -= subtract;
                            var score = Score();
                            if (score < best - 1e-10) { best = score; improved = true; }
                            else { foreach (var i in groups[a]) tracks[i] -= add; foreach (var i in groups[b]) tracks[i] += subtract; }
                        }
                if (!improved) break;
            }
        return (rows, columns);
    }
}
