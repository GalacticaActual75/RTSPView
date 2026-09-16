using System.Windows.Controls;
using RTSPView.Core;

namespace RTSPView.Viewer;

public static class CameraWallPresentation
{
    public static void Apply(Grid grid, IReadOnlyList<CameraTile> tiles, WallLayout layout, int? focusedSlot)
    {
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        var proportions = WallProportions.Calculate(layout);
        for (var row = 0; row < (focusedSlot.HasValue ? 1 : layout.Rows); row++)
            grid.RowDefinitions.Add(new() { Height = new System.Windows.GridLength(focusedSlot.HasValue ? 1 : proportions.Rows[row], System.Windows.GridUnitType.Star) });
        for (var column = 0; column < (focusedSlot.HasValue ? 1 : layout.Columns); column++)
            grid.ColumnDefinitions.Add(new() { Width = new System.Windows.GridLength(focusedSlot.HasValue ? 1 : proportions.Columns[column], System.Windows.GridUnitType.Star) });
        for (var index = 0; index < tiles.Count; index++)
        {
            var slot = index < AppSettings.MainCameraSlots.Length ? AppSettings.MainCameraSlots[index] : tiles[index].Slot;
            var placement = focusedSlot.HasValue
                ? (focusedSlot == slot ? new WallTile { CameraSlot = slot } : null)
                : layout.Tiles.FirstOrDefault(item => item.CameraSlot == slot);
            var tile = tiles[index];
            if (placement is null) { tile.SetWallVisibility(false); continue; }
            Grid.SetRow(tile, placement.Row); Grid.SetColumn(tile, placement.Column);
            Grid.SetRowSpan(tile, placement.RowSpan); Grid.SetColumnSpan(tile, placement.ColumnSpan);
            tile.SetWallSizing(focusedSlot.HasValue ? placement with { Sizing = "fit" } : placement,
                focusedSlot.HasValue ? layout.EffectiveWidth : layout.EffectiveWidth * proportions.Columns.Skip(placement.Column).Take(placement.ColumnSpan).Sum());
            tile.SetWallVisibility(true);
        }
    }
}
