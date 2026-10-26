using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics;

/// <summary>Creates and retires programs through the stage's selected graphics runtime.</summary>
public sealed class GraphicsProgramStage : IResourceTransform<GraphicsProgramArtifacts, GraphicsProgramResource, GraphicsResourceRuntime>
{
    /// <inheritdoc/>
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Resident;

    /// <inheritdoc/>
    public ResourceAddress GetInput(ResourceAddress output) => output;

    /// <inheritdoc/>
    public async ValueTask<GraphicsProgramResource> BuildAsync(GraphicsProgramArtifacts input, GraphicsResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        GraphicsDeviceLease epoch = await runtime.AcquireAsync(context, cancellationToken: cancellationToken);
        var resource = new GraphicsProgramResource(runtime, epoch.Device, epoch.State.Epoch);
        context.RegisterCleanup(resource.ReleaseAsync, ResourceDependencyLifetime.Resident);
        resource.Create(input, cancellationToken);
        return resource;
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(GraphicsProgramResource resource, GraphicsResourceRuntime runtime) => resource.ReleaseAsync();
}
