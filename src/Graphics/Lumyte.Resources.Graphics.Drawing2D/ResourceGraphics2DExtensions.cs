using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Resources.Graphics.Drawing2D;

/// <summary>Loads shared 2D renderer resources and creates renderers without exposing shader plumbing.</summary>
public static class ResourceGraphics2DExtensions
{
    private static readonly ResourceKey<Renderer2DResource> _key = new("Lumyte/Renderer2D");

    /// <summary>Preloads the configured renderer resources and follows later published generations.</summary>
    /// <param name="manager">The manager configured with AddGraphics2D.</param>
    /// <param name="cancellationToken">Cancels this acquisition.</param>
    /// <returns>The renderer factory resource reference.</returns>
    public static ValueTask<ResourceReference<Renderer2DResource>> AcquireRenderer2DAsync(this ResourceManager manager, CancellationToken cancellationToken = default)
        => manager.AcquireAsync(_key, cancellationToken);

    /// <summary>Creates an independent renderer retaining one exact shader and device generation.</summary>
    /// <param name="reference">The preloaded renderer factory resource.</param>
    /// <param name="renderers">The borrowed strategy table, or new stateless defaults when omitted.</param>
    /// <param name="snapshot">An optional frame publication shared with other resources.</param>
    /// <returns>A renderer owning its resource protection; dispose prepared draws only after GPU completion.</returns>
    /// <remarks>No I/O or GPU allocation occurs here. Dispose does not return resources retained by prepared draws.</remarks>
    public static Renderer2D CreateRenderer(this ResourceReference<Renderer2DResource> reference, DrawingCommandRenderers2D? renderers = null, ResourceSnapshot? snapshot = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ResourceLease<Renderer2DResource> lease = reference.Acquire(snapshot);
        try
        {
            Renderer2DResource resource = lease.Value;
            return new Renderer2D(
                resource.Device,
                renderers ?? DrawingCommandRenderers2D.CreateDefault(),
                resource.Resources,
                resourceOwner: lease,
                validateResources: () => _ = lease.Value);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
