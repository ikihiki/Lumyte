namespace Lumyte.Resources;

/// <summary>Describes the outcome of an atomic source or runtime rebuild.</summary>
public enum ResourceReloadStatus
{
    /// <summary>The observed revisions did not require an update.</summary>
    Unchanged,

    /// <summary>The complete candidate graph was published.</summary>
    Published,

    /// <summary>A source, runtime dependency or publication changed before the candidates could publish.</summary>
    Conflict,
}
