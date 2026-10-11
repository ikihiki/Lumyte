namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes an immutable shader program independently of draw state.</summary>
public sealed record GraphicsPipelineDesc
{
    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the vertex module; the caller manages its lifetime.</summary>
    public required IGraphicsShader VertexShader { get; init; }

    /// <summary>Gets the optional fragment module.</summary>
    public IGraphicsShader? FragmentShader { get; init; }

    /// <summary>Gets the primitive compilation class.</summary>
    public PrimitiveTopologyClass TopologyClass { get; init; } = PrimitiveTopologyClass.Triangle;

    /// <summary>Gets a value indicating whether alpha affects sample coverage.</summary>
    public bool AlphaToCoverageEnable { get; init; }

    /// <summary>Gets the native optimization hint.</summary>
    public PipelineOptimizationMode Optimization { get; init; } = PipelineOptimizationMode.PreferReuse;
}
