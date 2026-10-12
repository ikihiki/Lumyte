namespace Lumyte.Graphics.RenderGraph;

/// <summary>Describes a live ordering edge.</summary>
/// <param name="Before">The preceding execution index.</param>
/// <param name="After">The subsequent execution index.</param>
public sealed record RenderGraphDependencyInfo(int Before, int After);
