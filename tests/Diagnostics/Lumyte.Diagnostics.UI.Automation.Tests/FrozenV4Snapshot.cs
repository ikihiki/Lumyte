namespace Lumyte.Diagnostics.UI.Automation.Tests;

// Frozen v4 contract, independent of production DTOs.
internal sealed record FrozenV4Snapshot(Guid SurfaceId, string Sequence, FrozenV4Node[] Nodes);
