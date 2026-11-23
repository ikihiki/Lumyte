namespace Lumyte.UI.Collections;

/// <summary>Immutable cell template arguments.</summary>
/// <typeparam name="T">The row type.</typeparam>
/// <param name="Row">The copied row value.</param>
/// <param name="RowKey">Ordinal row identity.</param>
/// <param name="RowIndex">Projected zero-based row index.</param>
/// <param name="Column">Immutable column settings.</param>
/// <param name="ColumnIndex">Zero-based column index.</param>
public sealed record DataGridCellContext<T>(T Row, string RowKey, int RowIndex, DataGridColumn<T> Column, int ColumnIndex)
    where T : notnull;
