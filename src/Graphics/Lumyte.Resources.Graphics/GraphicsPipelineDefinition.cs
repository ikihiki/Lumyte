using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Defines a graphics program using shader dependencies and all pipeline compilation settings.</summary>
public sealed class GraphicsPipelineDefinition : GraphicsResourceDefinition<IGraphicsPipeline>
{
    /// <summary>Gets the vertex shader resource.</summary>
    public required ResourceKey<GraphicsResource<IGraphicsShader>> VertexShader { get; init; }

    /// <summary>Gets the optional fragment shader resource.</summary>
    public ResourceKey<GraphicsResource<IGraphicsShader>>? FragmentShader { get; init; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the primitive compilation class.</summary>
    public PrimitiveTopologyClass TopologyClass { get; init; } = PrimitiveTopologyClass.Triangle;

    /// <summary>Gets a value indicating whether alpha affects sample coverage.</summary>
    public bool AlphaToCoverageEnable { get; init; }

    /// <summary>Gets the native optimization hint.</summary>
    public PipelineOptimizationMode Optimization { get; init; } = PipelineOptimizationMode.PreferReuse;

    /// <inheritdoc/>
    public override async ValueTask<IGraphicsPipeline> CreateAsync(IGraphicDevice device, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        IGraphicsShader vertex = await GraphicsResourceDependency.RequireAsync(VertexShader, device, context, cancellationToken);
        IGraphicsShader? fragment = FragmentShader is { } key ? await GraphicsResourceDependency.RequireAsync(key, device, context, cancellationToken) : null;
        cancellationToken.ThrowIfCancellationRequested();
        return device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment, Label = Label, TopologyClass = TopologyClass, AlphaToCoverageEnable = AlphaToCoverageEnable, Optimization = Optimization });
    }
}
