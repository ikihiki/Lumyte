namespace Lumyte.Resources;

/// <summary>Captures availability and irreversible invalidation of a runtime provider.</summary>
/// <param name="Version">The monotonically increasing state version.</param>
/// <param name="IsAvailable">Whether the current version can support new builds.</param>
/// <param name="InvalidatedThroughVersion">The largest irreversibly unusable version.</param>
public readonly record struct ResourceRuntimeSnapshot(ulong Version, bool IsAvailable, ulong InvalidatedThroughVersion);
