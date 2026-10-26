using Lumyte.Graphics.Hosting;

namespace Lumyte.Resources.Graphics;

/// <summary>Builds registered resources by key and releases them on their original epoch.</summary>
/// <typeparam name="T">The owned native output.</typeparam>
public sealed class GraphicsResourceStage<T> : IResourceSourceStage<GraphicsResource<T>, GraphicsResourceRuntime>
    where T : class, IDisposable
{
    private readonly Dictionary<ResourceAddress, GraphicsResourceDefinition<T>> _definitions = [];

    /// <summary>Initializes a new instance of the <see cref="GraphicsResourceStage{T}"/> class.</summary>
    /// <param name="definitions">Definitions in registration order; the last value for each key wins.</param>
    public GraphicsResourceStage(IEnumerable<GraphicsResourceRegistration<T>> definitions)
    {
        foreach (GraphicsResourceRegistration<T> registration in definitions)
        {
            _definitions[registration.Key.Address] = registration.Definition;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<GraphicsResource<T>> BuildAsync(GraphicsResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        if (!_definitions.TryGetValue(context.Address, out GraphicsResourceDefinition<T>? definition))
        {
            throw new InvalidOperationException("No graphics resource definition is registered for this key.");
        }

        GraphicsDeviceLease device = await runtime.AcquireAsync(context, cancellationToken: cancellationToken);
        T native = await definition.CreateAsync(device.Device, context, cancellationToken);
        var result = new GraphicsResource<T>(native, runtime, device.Device, device.State.Epoch);
        context.RegisterCleanup(result.ReleaseAsync, ResourceDependencyLifetime.Resident);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(GraphicsResource<T> resource, GraphicsResourceRuntime runtime) => resource.ReleaseAsync();
}
