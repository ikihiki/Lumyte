using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Defines a compute pipeline using a tracked shader dependency.</summary>
/// <param name="shader">The compute shader resource.</param>
/// <param name="label">The optional diagnostic label.</param>
public sealed class GraphicsComputePipelineDefinition(ResourceKey<GraphicsResource<IGraphicsShader>> shader, string? label = null) : GraphicsResourceDefinition<IGraphicsComputePipeline>
{
    /// <inheritdoc/>
    public override async ValueTask<IGraphicsComputePipeline> CreateAsync(IGraphicDevice device, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        IGraphicsShader compute = await GraphicsResourceDependency.RequireAsync(shader, device, context, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return device.CreateComputePipeline(new() { ComputeShader = compute, Label = label });
    }
}
