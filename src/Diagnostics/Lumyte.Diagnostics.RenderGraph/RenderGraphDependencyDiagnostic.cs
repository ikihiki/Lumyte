namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>A detached live ordering dependency.</summary>
/// <param name="Before">The Before value.</param>
/// <param name="After">The After value.</param>
public sealed record RenderGraphDependencyDiagnostic(long Before, long After);
