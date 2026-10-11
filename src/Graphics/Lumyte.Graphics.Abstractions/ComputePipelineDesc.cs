namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes a compute program using artifact workgroup metadata.</summary>
public sealed record ComputePipelineDesc
{
    /// <summary>Gets the diagnostic label.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the compute module; the caller manages its lifetime.</summary>
    public required IGraphicsShader ComputeShader { get; init; }
}
