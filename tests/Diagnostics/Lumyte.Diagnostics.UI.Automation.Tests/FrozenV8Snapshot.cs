namespace Lumyte.Diagnostics.UI.Automation.Tests;

// Frozen v8 contract, independent of production DTOs.
internal sealed record FrozenV8Snapshot(Guid SurfaceId, string Sequence, FrozenV8Node[] Nodes);
