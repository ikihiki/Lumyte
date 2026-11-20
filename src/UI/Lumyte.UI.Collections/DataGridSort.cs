namespace Lumyte.UI.Collections;

/// <summary>Immutable single-column ordering.</summary>
/// <param name="ColumnKey">Column identity, or null for source order.</param>
/// <param name="Direction">Ordering direction.</param>
public sealed record DataGridSort(string? ColumnKey, DataGridSortDirection Direction);
