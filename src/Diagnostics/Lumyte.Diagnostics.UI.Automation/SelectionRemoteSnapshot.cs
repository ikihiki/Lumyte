namespace Lumyte.Diagnostics.UI.Automation;

internal sealed record SelectionRemoteSnapshot(Guid SurfaceId, string Sequence, SelectionRemoteNode[] Nodes);
