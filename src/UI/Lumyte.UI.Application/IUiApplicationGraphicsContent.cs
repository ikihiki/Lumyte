namespace Lumyte.UI.Application;

/// <summary>Rebuilds custom borrowed GPU views before drawing with a recovered device.</summary>
public interface IUiApplicationGraphicsContent
{
    /// <summary>Updates GPU-dependent content on the UI owner after resource runtime rebuilding.</summary>
    /// <param name="context">The retained CPU view and window.</param>
    /// <returns>Completion before new drawing is admitted.</returns>
    ValueTask RebuildGraphicsAsync(UiApplicationWindowContext context);
}
