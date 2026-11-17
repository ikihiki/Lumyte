namespace Lumyte.Diagnostics.UI.Automation;

internal sealed record NumericRemoteSnapshot(Guid SurfaceId, string Sequence, NumericRemoteNode[] Nodes);
