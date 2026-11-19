namespace Lumyte.Diagnostics.UI.Automation;

internal sealed record ExpandedRemoteSnapshot(Guid SurfaceId, string Sequence, ExpandedRemoteNode[] Nodes);
