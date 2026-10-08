namespace Lumyte.Graphics;

/// <summary>
/// Owns logical descriptor registrations; the backend resolves their physical bindings automatically.
/// </summary>
public interface IArgumentTable : IDisposable
{
    /// <summary>Gets the logical texture capacity.</summary>
    uint TextureCapacity { get; }

    /// <summary>Gets the logical sampler capacity.</summary>
    uint SamplerCapacity { get; }

    /// <summary>Gets the logical buffer capacity.</summary>
    uint BufferCapacity { get; }

    /// <summary>Registers a sampled view in an idle logical slot without uploading pixels.</summary>
    /// <param name="slot">The caller-managed logical registration slot.</param>
    /// <param name="view">The sampled view from this device.</param>
    /// <returns>The opaque registration reference.</returns>
    TextureDescriptorReference WriteTexture(uint slot, IGraphicsTextureView view);

    /// <summary>Registers a sampler independently of textures.</summary>
    /// <param name="slot">The caller-managed logical registration slot.</param>
    /// <param name="sampler">The immutable sampler from this device.</param>
    /// <returns>The opaque registration reference.</returns>
    SamplerDescriptorReference WriteSampler(uint slot, Sampler sampler);

    /// <summary>Registers a read-only storage range without copying its contents.</summary>
    /// <typeparam name="T">The unmanaged buffer element type.</typeparam>
    /// <param name="slot">The caller-managed logical registration slot.</param>
    /// <param name="range">The read-only shader range from this device.</param>
    /// <returns>The opaque registration reference.</returns>
    BufferDescriptorReference<T> WriteBuffer<T>(uint slot, BufferSlice<T> range)
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
