namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>A detached semantic tree with decimal UInt64 sequence.</summary>
/// <param name="SurfaceId">The SurfaceId value.</param>
/// <param name="Sequence">The Sequence value.</param>
/// <param name="Nodes">The Nodes value.</param>
public sealed record UiRemoteSnapshot(Guid SurfaceId, string Sequence, UiRemoteNode[] Nodes);
