namespace RTSPView.Core;

public sealed record WallTile
{
    public int CameraSlot { get; init; }
    public int Row { get; init; }
    public int Column { get; init; }
    public int RowSpan { get; init; } = 1;
    public int ColumnSpan { get; init; } = 1;
}

public sealed record WallLayout
{
    public string Id { get; init; } = "default";
    public string Name { get; init; } = "Default";
    public int Rows { get; init; } = 3;
    public int Columns { get; init; } = 3;
    public string AspectRatio { get; init; } = "16:9";

    public (double Width, double Height) Fit(double availableWidth, double availableHeight)
    {
        var ratio = AspectRatio == "9:16" ? 9d / 16 : 16d / 9;
        var width = Math.Min(Math.Max(0, availableWidth), Math.Max(0, availableHeight) * ratio);
        return (width, width / ratio);
    }
    public IReadOnlyList<WallTile> Tiles { get; init; } = Enumerable.Range(0, 9)
        .Select(i => new WallTile { CameraSlot = i + 1, Row = i / 3, Column = i % 3 }).ToArray();

    public static void Validate(IReadOnlyList<WallLayout>? layouts, string? activeId)
    {
        if (layouts is null || layouts.Count is < 1 or > 32)
            throw new InvalidDataException("Keep between 1 and 32 saved layouts.");
        var ids = new HashSet<string>();
        foreach (var layout in layouts)
        {
            if (layout is null || string.IsNullOrWhiteSpace(layout.Id) || layout.Id.Length > 64 || !ids.Add(layout.Id) ||
                string.IsNullOrWhiteSpace(layout.Name) || layout.Name.Length > 80)
                throw new InvalidDataException("Layouts need unique IDs and names of 1–80 characters.");
            if (layout.Rows is < 1 or > 4 || layout.Columns is < 1 or > 4 || layout.Tiles is null || layout.Tiles.Count is < 1 or > 16)
                throw new InvalidDataException("Layouts support 1–4 rows and columns and 1–16 camera tiles.");
            if (layout.AspectRatio is not ("16:9" or "9:16"))
                throw new InvalidDataException("Choose landscape (16:9) or portrait (9:16).");
            var occupied = new HashSet<(int, int)>();
            var cameras = new HashSet<int>();
            foreach (var tile in layout.Tiles)
            {
                if (tile is null || !AppSettings.MainCameraSlots.Contains(tile.CameraSlot) || !cameras.Add(tile.CameraSlot))
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
