namespace Lumyte.Graphics.Abstractions;

/// <summary>
/// Owns logical descriptor registrations; physical binding resolution consumes these registrations.
/// </summary>
/// <remarks>The caller manages all required lifetime and access synchronization.</remarks>
public interface IArgumentTable : IDisposable
{
    /// <summary>Gets the logical texture capacity.</summary>
    uint TextureCapacity { get; }

    /// <summary>Gets the logical sampler capacity.</summary>
    uint SamplerCapacity { get; }

    /// <summary>Gets the logical buffer capacity.</summary>
    uint BufferCapacity { get; }

    /// <summary>Registers a sampled view in a logical slot without uploading pixels.</summary>
    /// <param name="slot">The caller-managed logical registration slot.</param>
    /// <param name="view">The sampled view from this device.</param>
    /// <returns>The opaque registration reference.</returns>
    IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view);

    /// <summary>Registers a sampler independently of textures.</summary>
    /// <param name="slot">The caller-managed logical registration slot.</param>
    /// <param name="sampler">The immutable sampler from this device.</param>
    /// <returns>The opaque registration reference.</returns>
    IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler);

    /// <summary>Registers a shader storage range without copying its contents.</summary>
    /// <typeparam name="T">The unmanaged buffer element type.</typeparam>
    /// <param name="slot">The caller-managed logical registration slot.</param>
    /// <param name="range">The shader range from this device.</param>
    /// <returns>The opaque registration reference.</returns>
    IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range)
        where T : unmanaged;

    /// <summary>Releases an idle texture slot and invalidates its old references.</summary>
    /// <param name="slot">The caller-managed logical registration slot.</param>
    void ReleaseTexture(uint slot);

    /// <summary>Releases an idle sampler slot and invalidates its old references.</summary>
    /// <param name="slot">The caller-managed logical registration slot.</param>
    void ReleaseSampler(uint slot);

    /// <summary>Releases an idle buffer slot and invalidates its old references.</summary>
    /// <param name="slot">The caller-managed logical registration slot.</param>
    void ReleaseBuffer(uint slot);
}
