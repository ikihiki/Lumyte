namespace Lumyte.UI;

/// <summary>An actual read-only table viewport, independent of role labels.</summary>
public interface IUiGridViewport : IUiItemViewport
{
    /// <summary>Queues row and column visibility on the idle owner thread.</summary>
    /// <param name="rowKey">The adopted ordinal row identity.</param>
    /// <param name="columnKey">The visible ordinal column identity.</param>
    /// <returns>Whether both keys belong to this mounted model.</returns>
    bool BringCellIntoView(string rowKey, string columnKey);
}
