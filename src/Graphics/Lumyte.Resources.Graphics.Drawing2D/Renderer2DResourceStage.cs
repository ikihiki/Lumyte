using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics.Drawing2D;

/// <summary>Composes renderer factory resources from generic keyed Graphics definitions.</summary>
public sealed class Renderer2DResourceStage : IResourceSourceStage<Renderer2DResource, GraphicsResourceRuntime>
{
    /// <inheritdoc/>
    public async ValueTask<Renderer2DResource> BuildAsync(GraphicsResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        GraphicsDeviceLease epoch = await runtime.AcquireAsync(context, cancellationToken: cancellationToken);
        GraphicsResource<IGraphicsPipeline> pipeline = await context.RequireAsync(DrawingGraphicsResources2D.Pipeline, cancellationToken: cancellationToken);
        GraphicsResource<IGraphicsSampler> sampler = await context.RequireAsync(DrawingGraphicsResources2D.Sampler, cancellationToken: cancellationToken);
        if (!ReferenceEquals(epoch.Device, pipeline.Device) || !ReferenceEquals(epoch.Device, sampler.Device))
        {
            throw new InvalidOperationException("2D and its shared resources must use the selected physical device.");
        }

        if (pipeline.Value.Desc.TopologyClass != PrimitiveTopologyClass.Triangle || pipeline.Value.Desc.AlphaToCoverageEnable)
        {
            throw new InvalidOperationException("2D drawing requires a triangle pipeline without alpha-to-coverage.");
        }

        if (pipeline.Value.Desc.FragmentShader is null)
        {
            throw new InvalidOperationException("2D drawing requires a fragment shader.");
        }

        return new(new(epoch.Device, pipeline.Value, sampler.Value));
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(Renderer2DResource resource, GraphicsResourceRuntime runtime) => ValueTask.CompletedTask;
}
