using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

internal static class GraphicsResourceDependency
{
    internal static async ValueTask<T> RequireAsync<T>(ResourceKey<GraphicsResource<T>> key, IGraphicDevice device, ResourceBuildContext context, CancellationToken cancellationToken)
        where T : class, IDisposable
    {
        GraphicsResource<T> resource = await context.RequireAsync(key, cancellationToken: cancellationToken);
        if (!ReferenceEquals(device, resource.Device))
        {
            throw new InvalidOperationException("Graphics dependencies must belong to the selected physical device.");
        }

        return resource.Value;
    }
}
