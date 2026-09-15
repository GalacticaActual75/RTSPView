namespace RTSPView.Core;

// Balance shared grid tracks toward 16:9 camera pictures. Grid placement and
// spans stay intact; video rendering remains contain/fit and never crops.
public static class WallProportions
{
    public static (double[] Rows, double[] Columns) Calculate(WallLayout layout)
    {
        var rows = Enumerable.Repeat(1d / layout.Rows, layout.Rows).ToArray();
        var columns = Enumerable.Repeat(1d / layout.Columns, layout.Columns).ToArray();
        var aspect = layout.AspectRatio == "9:16" ? 9d / 16 : 16d / 9;
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
                foreach (var tracks in new[] { rows, columns })
                    for (var a = 0; a < tracks.Length; a++)
                        for (var b = 0; b < tracks.Length; b++)
                        {
                            if (a == b || tracks[b] - step < .35 / tracks.Length || tracks[a] + step > 2d / tracks.Length) continue;
                            tracks[a] += step; tracks[b] -= step;
                            var score = Score();
                            if (score < best - 1e-10) { best = score; improved = true; }
                            else { tracks[a] -= step; tracks[b] += step; }
                        }
                if (!improved) break;
            }
        return (rows, columns);
    }
}
