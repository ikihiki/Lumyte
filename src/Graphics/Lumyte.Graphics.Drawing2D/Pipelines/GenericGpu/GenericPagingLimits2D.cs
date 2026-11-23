namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Explicit finite inventories for opt-in generic GPU paging; never a quality setting.</summary>
public sealed record GenericPagingLimits2D
{
    /// <summary>Initializes a new instance of the <see cref="GenericPagingLimits2D"/> class. Does not change any canonical extent.</summary>
    /// <param name="maxWindows">Maximum immutable output-window tables, from one through 4096.</param>
    /// <param name="maxScenePages">Maximum arbitrary-read source pages, from one through three.</param>
    public GenericPagingLimits2D(int maxWindows = 512, int maxScenePages = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWindows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxWindows, 4096);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxScenePages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxScenePages, 3);
        MaxWindows = maxWindows;
        MaxScenePages = maxScenePages;
    }

    /// <summary>Gets the finite immutable output-window table ceiling.</summary>
    public int MaxWindows { get; }

    /// <summary>Gets the finite arbitrary-read scene-page ceiling.</summary>
    public int MaxScenePages { get; }
}
