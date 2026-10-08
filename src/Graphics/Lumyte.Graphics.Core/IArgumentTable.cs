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
    IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view);

    /// <summary>Registers a sampler independently of textures.</summary>
    /// <param name="slot">The caller-managed logical registration slot.</param>
    /// <param name="sampler">The immutable sampler from this device.</param>
    /// <returns>The opaque registration reference.</returns>
    IGpuRef<Sampler> WriteSampler(uint slot, Sampler sampler);

    /// <summary>Registers a shader storage range without copying its contents.</summary>
    /// <typeparam name="T">The unmanaged buffer element type.</typeparam>
    /// <param name="slot">The caller-managed logical registration slot.</param>
    /// <param name="range">The shader range from this device.</param>
    /// <returns>The opaque registration reference.</returns>
    IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range)
        where T : unmanaged;

    /// <summary>Registers completed serialized shader data with its reflected logical element layout.</summary>
    /// <typeparam name="T">The caller-owned logical element type.</typeparam>
    /// <param name="slot">The caller-managed buffer registration slot.</param>
    /// <param name="range">The exact element-aligned serialized byte range.</param>
    /// <param name="layout">The reflected wire layout used to serialize these elements.</param>
    /// <returns>The opaque range reference; GetElement selects an individual logical element.</returns>
    IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<byte> range, ShaderDataLayout<T> layout);

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
