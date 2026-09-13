namespace Lumyte.Graphics.Portable.Resources;

/// <summary>Owns whole textures and lends them by the complete, exact creation description.</summary>
/// <remarks>
/// The backend is borrowed. The caller serializes pool operations in the backend's execution context,
/// ends all view, binding, recording and GPU uses before returning a lease, and never destroys its handle directly.
/// Reusing a texture does not reset its contents or GPU state. No heap placement or automatic retirement occurs.
/// </remarks>
public sealed class GpuTexturePool : IDisposable
{
    private readonly ResourcePool<GpuTextureDescription, GpuTextureHandle> pool;

    public GpuTexturePool(IPortableGpuBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        pool = new(backend.CreateTexture, backend.DestroyTexture, nameof(GpuTexturePool));
    }

    /// <summary>Lends an unused matching texture or creates one using the unchanged description.</summary>
    public GpuTextureLease Acquire(GpuTextureDescription description)
        => pool.Acquire(description, static loan => new GpuTextureLease(loan));

    /// <summary>Returns this exact loan after the caller has ended all references and uses.</summary>
    public void Release(GpuTextureLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        pool.Release(lease.Loan);
    }

    /// <summary>Attempts to destroy every idle texture once; active loans remain untouched.</summary>
    public void Trim() => pool.Trim();

    /// <summary>Rejects outstanding loans; otherwise attempts every idle destruction once without owning the backend.</summary>
    public void Dispose() => pool.Dispose();
}
