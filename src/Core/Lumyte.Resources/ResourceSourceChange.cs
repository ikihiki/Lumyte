namespace Lumyte.Resources;

/// <summary>Signals that a source may have changed; the revision is only a hint for revalidation.</summary>
/// <param name="Id">The potentially changed source identifier.</param>
/// <param name="Revision">The observed revision, or null for deletion or an unknown revision.</param>
public readonly record struct ResourceSourceChange(ResourceId Id, string? Revision)
{
    /// <summary>Gets a hint to recheck every tracked source, for example after watcher overflow or a directory move.</summary>
    /// <remarks>The identifier and revision are unused for this hint.</remarks>
    public static ResourceSourceChange Rescan => new(default, null) { RequiresRescan = true };

    /// <summary>Gets a value indicating whether all tracked sources must be rechecked instead of the individual identifier.</summary>
    public bool RequiresRescan { get; init; }
}
