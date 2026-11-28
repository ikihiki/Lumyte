namespace Lumyte.Pipelines;

/// <summary>A typed edge identifier local to a definition visit.</summary>
/// <typeparam name="T">The immutable payload.</typeparam>
/// <param name="Index">Zero for the input, otherwise the one-based producer index.</param>
public readonly record struct PipelineEdge<T>(int Index);
