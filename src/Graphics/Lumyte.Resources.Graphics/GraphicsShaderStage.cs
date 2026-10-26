using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics;

/// <summary>Creates and retires shader modules on their selected graphics environment.</summary>
public sealed class GraphicsShaderStage : IResourceTransform<ShaderArtifact, GraphicsShaderResource, GraphicsResourceRuntime>
{
    /// <inheritdoc/>
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Resident;

    /// <inheritdoc/>
    public ResourceAddress GetInput(ResourceAddress output) => output;

    /// <inheritdoc/>
    public async ValueTask<GraphicsShaderResource> BuildAsync(ShaderArtifact input, GraphicsResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        GraphicsDeviceLease lease = await runtime.AcquireAsync(context, cancellationToken: cancellationToken);
        IGraphicDevice device = lease.Device;
        IGraphicsShader shader = device.CreateShader(input);
        try
        {
            return new(shader, device, lease.State.Epoch);
        }
        catch
        {
            shader.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public async ValueTask ReleaseAsync(GraphicsShaderResource resource, GraphicsResourceRuntime runtime)
    {
        if (resource.Device.Status == GraphicsDeviceStatus.Lost)
        {
            await runtime.WaitForRetirementAsync(resource.Epoch);
        }

        resource.Shader.Dispose();
    }
}
