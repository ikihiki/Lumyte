namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes a mesh program borrowing its same-device shader modules.</summary>
public sealed record MeshPipelineDesc
{
    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the required mesh module; the caller manages its lifetime.</summary>
    public required IGraphicsShader MeshShader { get; init; }

    /// <summary>Gets the optional task module.</summary>
    public IGraphicsShader? TaskShader { get; init; }

    /// <summary>Gets the optional fragment module.</summary>
    public IGraphicsShader? FragmentShader { get; init; }

    /// <summary>Gets a value indicating whether alpha affects sample coverage.</summary>
    public bool AlphaToCoverageEnable { get; init; }

    /// <summary>Gets the native optimization hint.</summary>
    public PipelineOptimizationMode Optimization { get; init; } = PipelineOptimizationMode.PreferReuse;
}
