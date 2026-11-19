namespace Lumyte.UI;

/// <summary>Detached read-only cell coordinates, independent of realization identities.</summary>
/// <param name="RowKey">Ordinal source row identity.</param>
/// <param name="ColumnKey">Ordinal column identity.</param>
/// <param name="RowIndex">Projected zero-based row index.</param>
/// <param name="ColumnIndex">Zero-based visible column index.</param>
public sealed record UiGridCellState(string RowKey, string ColumnKey, int RowIndex, int ColumnIndex);
