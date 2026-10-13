namespace Lumyte.Resources;

/// <summary>Signals that a source may have changed; the revision is only a hint for revalidation.</summary>
/// <param name="Id">The potentially changed source identifier.</param>
/// <param name="Revision">The observed revision, or null for deletion or an unknown revision.</param>
public readonly record struct ResourceSourceChange(ResourceId Id, string? Revision);
