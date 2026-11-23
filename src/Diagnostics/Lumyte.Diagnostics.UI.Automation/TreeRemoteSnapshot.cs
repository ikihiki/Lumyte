namespace Lumyte.Diagnostics.UI.Automation;

internal sealed record TreeRemoteSnapshot(Guid SurfaceId, string Sequence, TreeRemoteNode[] Nodes);
