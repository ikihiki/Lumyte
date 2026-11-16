namespace Lumyte.Diagnostics.UI.Automation;

internal sealed record LegacyRemoteSnapshot(Guid SurfaceId, string Sequence, LegacyRemoteNode[] Nodes);
