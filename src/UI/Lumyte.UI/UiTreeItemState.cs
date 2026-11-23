namespace Lumyte.UI;

/// <summary>Logical hierarchy metadata independent of connected ancestor IDs.</summary>
/// <param name="Depth">Zero-based model depth.</param>
/// <param name="ParentKey">Ordinal logical parent key, null for roots.</param>
public sealed record UiTreeItemState(int Depth, string? ParentKey);
