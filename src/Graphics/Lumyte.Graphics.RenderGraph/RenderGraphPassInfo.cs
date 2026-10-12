namespace Lumyte.Graphics.RenderGraph;

/// <summary>Describes a declared pass; execution index -1 denotes a culled pass.</summary>
/// <param name="Name">The pass name.</param>
/// <param name="ExecutionIndex">The index in live execution order.</param>
/// <param name="HasSideEffects">Whether the pass is an explicit or imported-write root.</param>
public sealed record RenderGraphPassInfo(string Name, int ExecutionIndex, bool HasSideEffects);
