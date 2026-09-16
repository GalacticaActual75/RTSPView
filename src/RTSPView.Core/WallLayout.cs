namespace RTSPView.Core;

public sealed record WallTile
{
    public int CameraSlot { get; init; }
    public int Row { get; init; }
    public int Column { get; init; }
    public int RowSpan { get; init; } = 1;
    public int ColumnSpan { get; init; } = 1;
    public string Sizing { get; init; } = "original";
    public int ZoomPercent { get; init; } = 100;
    public int HorizontalPositionPercent { get; init; } = 50;
    public int VerticalPositionPercent { get; init; } = 50;
}

public sealed record WallLayout
{
    public string Id { get; init; } = "default";
    public string Name { get; init; } = "Default";
    public int Rows { get; init; } = 3;
    public int Columns { get; init; } = 3;
    public string AspectRatio { get; init; } = "16:9";
    public int OutputWidth { get; init; }
    public int OutputHeight { get; init; }
    public int EffectiveWidth => OutputWidth > 0 ? OutputWidth : AspectRatio == "9:16" ? 1080 : 1920;
    public int EffectiveHeight => OutputHeight > 0 ? OutputHeight : AspectRatio == "9:16" ? 1920 : 1080;
    // Automation layouts use -1/-2 for unassigned focus tiles; positive IDs are migrated legacy positions.
    public double[] RowWeights { get; init; } = [];
    public double[] ColumnWeights { get; init; } = [];
    public int[] FocusSlots { get; init; } = [];

    public (double Width, double Height) Fit(double availableWidth, double availableHeight)
    {
        var ratio = (double)EffectiveWidth / EffectiveHeight;
        var width = Math.Min(Math.Max(0, availableWidth), Math.Max(0, availableHeight) * ratio);
        return (width, width / ratio);
    }
    public IReadOnlyList<WallTile> Tiles { get; init; } = Enumerable.Range(0, 9)
        .Select(i => new WallTile { CameraSlot = i + 1, Row = i / 3, Column = i % 3 }).ToArray();

    public static void Validate(IReadOnlyList<WallLayout>? layouts, string? activeId, int maximumDimension = 12, bool allowFocusTiles = false)
    {
        if (layouts is null || layouts.Count is < 1 or > 32)
            throw new InvalidDataException("Keep between 1 and 32 saved layouts.");
        var ids = new HashSet<string>();
        foreach (var layout in layouts)
        {
            if (layout is null || string.IsNullOrWhiteSpace(layout.Id) || layout.Id.Length > 64 || !ids.Add(layout.Id) ||
                string.IsNullOrWhiteSpace(layout.Name) || layout.Name.Length > 80)
                throw new InvalidDataException("Layouts need unique IDs and names of 1–80 characters.");
            if (layout.Rows < 1 || layout.Rows > maximumDimension || layout.Columns < 1 || layout.Columns > maximumDimension || layout.Tiles is null || layout.Tiles.Count > 16)
                throw new InvalidDataException($"Layouts support 1–{maximumDimension} rows and columns and up to 16 camera tiles.");
            if ((layout.OutputWidth != 0 || layout.OutputHeight != 0) &&
                (layout.OutputWidth is < 240 or > 16384 || layout.OutputHeight is < 240 or > 16384))
                throw new InvalidDataException("Output dimensions must both be between 240 and 16384 pixels.");
            if (layout.AspectRatio is not ("16:9" or "9:16"))
                throw new InvalidDataException("Choose landscape (16:9) or portrait (9:16).");
            foreach (var (weights, count) in new[] { (layout.RowWeights, layout.Rows), (layout.ColumnWeights, layout.Columns) })
                if (weights is null || (weights.Length != 0 && (weights.Length != count || weights.Any(w => !double.IsFinite(w) || w < .02 || w > 1) || Math.Abs(weights.Sum() - 1) > .00001)))
                    throw new InvalidDataException("Custom row and column sizes must be positive proportions totaling 100%.");
            if ((layout.RowWeights.Length == 0) != (layout.ColumnWeights.Length == 0))
                throw new InvalidDataException("Custom sizing needs both row and column proportions.");
            var occupied = new HashSet<(int, int)>();
            var cameras = new HashSet<int>();
            foreach (var tile in layout.Tiles)
            {
                if (tile?.Sizing is not ("original" or "fit" or "fill" or "stretch"))
                    throw new InvalidDataException("Choose original, fit, fill, or stretch for tile sizing.");
                if (tile.ZoomPercent is < 25 or > 400 || tile.HorizontalPositionPercent is < 0 or > 100 || tile.VerticalPositionPercent is < 0 or > 100)
                    throw new InvalidDataException("Tile zoom must be 25–400%; image positions must be 0–100%.");
                if (tile is null || !(AppSettings.MainCameraSlots.Contains(tile.CameraSlot) || StreamCatalog.IsOverlaySource(tile.CameraSlot) || (allowFocusTiles && tile.CameraSlot is -1 or -2)) || !cameras.Add(tile.CameraSlot))
                    throw new InvalidDataException("Each tile must reference a different main camera.");
                if (tile.Row < 0 || tile.Column < 0 || tile.RowSpan < 1 || tile.ColumnSpan < 1 ||
                    tile.RowSpan > layout.Rows || tile.ColumnSpan > layout.Columns ||
                    tile.Row > layout.Rows - tile.RowSpan || tile.Column > layout.Columns - tile.ColumnSpan)
                    throw new InvalidDataException("Tiles must fit inside the layout grid.");
                for (var r = tile.Row; r < tile.Row + tile.RowSpan; r++)
                    for (var c = tile.Column; c < tile.Column + tile.ColumnSpan; c++)
                        if (!occupied.Add((r, c))) throw new InvalidDataException("Camera tiles cannot overlap.");
            }
        }
        if (activeId is null || !ids.Contains(activeId)) throw new InvalidDataException("Choose a saved layout to display.");
    }
}

public sealed record WallLayoutsRequest
{
    public IReadOnlyList<WallLayout> Layouts { get; init; } = [];
    public string ActiveLayoutId { get; init; } = "default";
}
