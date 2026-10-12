namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>A detached pass inspection result.</summary>
/// <param name="Name">The Name value.</param>
/// <param name="ExecutionIndex">The ExecutionIndex value.</param>
/// <param name="HasSideEffects">The HasSideEffects value.</param>
public sealed record RenderGraphPassDiagnostic(string Name, long ExecutionIndex, bool HasSideEffects);
