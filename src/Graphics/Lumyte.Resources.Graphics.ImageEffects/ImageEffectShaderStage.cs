using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.ImageEffects;

namespace Lumyte.Resources.Graphics.ImageEffects;

/// <summary>Creates and retires a shared image-effect shader pair on its selected graphics environment.</summary>
public sealed class ImageEffectShaderStage : IResourceTransform<ImageEffectShaders, ImageEffectShaderResource, GraphicsResourceRuntime>
{
    /// <inheritdoc/>
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Resident;

    /// <inheritdoc/>
    public ResourceAddress GetInput(ResourceAddress output) => output;

    /// <inheritdoc/>
    public async ValueTask<ImageEffectShaderResource> BuildAsync(ImageEffectShaders input, GraphicsResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        GraphicsDeviceLease lease = await runtime.AcquireAsync(context, cancellationToken: cancellationToken);
        var resource = new ImageEffectShaderResource(runtime, lease.Device, lease.State.Epoch);
        context.RegisterCleanup(resource.ReleaseAsync, ResourceDependencyLifetime.Resident);
        resource.Create(input, cancellationToken);
        return resource;
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(ImageEffectShaderResource resource, GraphicsResourceRuntime runtime)
        => resource.ReleaseAsync();
}
