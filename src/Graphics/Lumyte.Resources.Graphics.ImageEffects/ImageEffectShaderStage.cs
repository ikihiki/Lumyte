using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics.ImageEffects;

/// <summary>Composes effect resources from the generic keyed Graphics resource definitions.</summary>
public sealed class ImageEffectShaderStage : IResourceSourceStage<ImageEffectShaderResource, GraphicsResourceRuntime>
{
    /// <inheritdoc/>
    public async ValueTask<ImageEffectShaderResource> BuildAsync(GraphicsResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        GraphicsDeviceLease epoch = await runtime.AcquireAsync(context, cancellationToken: cancellationToken);
        GraphicsResource<IGraphicsPipeline> pipeline = await context.RequireAsync(ImageEffectGraphicsResources.Pipeline, cancellationToken: cancellationToken);
        GraphicsResource<IGraphicsSampler> sampler = await context.RequireAsync(ImageEffectGraphicsResources.Sampler, cancellationToken: cancellationToken);
        if (!ReferenceEquals(epoch.Device, pipeline.Device) || !ReferenceEquals(epoch.Device, sampler.Device))
        {
            throw new InvalidOperationException("Effects and their shared resources must use the selected physical device.");
        }

        if (pipeline.Value.Desc.TopologyClass != PrimitiveTopologyClass.Triangle || pipeline.Value.Desc.AlphaToCoverageEnable)
        {
            throw new InvalidOperationException("Image effects require a triangle pipeline without alpha-to-coverage.");
        }

        return new(pipeline, sampler);
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(ImageEffectShaderResource resource, GraphicsResourceRuntime runtime) => ValueTask.CompletedTask;
}
