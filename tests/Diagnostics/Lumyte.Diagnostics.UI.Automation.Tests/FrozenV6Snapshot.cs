namespace Lumyte.Diagnostics.UI.Automation.Tests;

// Frozen v6 contract, independent of production DTOs.
internal sealed record FrozenV6Snapshot(Guid SurfaceId, string Sequence, FrozenV6Node[] Nodes);
