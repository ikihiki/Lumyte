namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>Detached read-only grid cell coordinates.</summary>
/// <param name="RowKey">Ordinal row identity.</param>
/// <param name="ColumnKey">Ordinal column identity.</param>
/// <param name="RowIndex">Projected zero-based row index.</param>
/// <param name="ColumnIndex">Zero-based column index.</param>
public sealed record UiRemoteGridCellState(string RowKey, string ColumnKey, int RowIndex, int ColumnIndex);
