namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns one-shot GPU command memory; the caller manages synchronization.</summary>
public interface IGraphicsCommandBuffer : IDisposable
{
    /// <summary>Gets the recording and execution state.</summary>
    CommandBufferState State { get; }

    /// <summary>Records a raw GPU buffer copy without CPU access or submission.</summary>
    /// <typeparam name="TSource">The source storage element type.</typeparam>
    /// <typeparam name="TDestination">The destination storage element type.</typeparam>
    /// <param name="source">The source value.</param>
    /// <param name="destination">The destination value.</param>
    void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged;

    /// <summary>Records a texture copy without filtering or format conversion.</summary>
    /// <param name="source">The source value.</param>
    /// <param name="destination">The destination value.</param>
    void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination);

    /// <summary>Records a buffer-to-texture copy using explicit row layout.</summary>
    /// <param name="source">The source value.</param>
    /// <param name="destination">The destination value.</param>
    void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination);

    /// <summary>Records a texture-to-buffer copy using explicit row layout.</summary>
    /// <param name="source">The source value.</param>
    /// <param name="destination">The destination value.</param>
    void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination);

    /// <summary>Records an explicit memory dependency outside passes.</summary>
    /// <param name="barrier">The barrier value.</param>
    void Barrier(MemoryBarrierDesc barrier);

    /// <summary>Records an explicit buffer range dependency.</summary>
    /// <typeparam name="T">The unmanaged storage element type.</typeparam>
    /// <param name="barrier">The barrier value.</param>
    void Barrier<T>(BufferBarrierDesc<T> barrier)
        where T : unmanaged;

    /// <summary>Records an explicit texture dependency and layout transition.</summary>
    /// <param name="barrier">The barrier value.</param>
    void Barrier(TextureBarrierDesc barrier);

    /// <summary>Begins a color rendering scope.</summary>
    /// <param name="desc">The desc value.</param>
    /// <returns>The non-owning pass handle.</returns>
    IRenderEncoder BeginRenderPass(RenderPassDesc desc);

    /// <summary>Begins a compute recording scope.</summary>
    /// <param name="desc">The desc value.</param>
    /// <returns>The non-owning pass handle.</returns>
    IComputeEncoder BeginComputePass(ComputePassDesc desc);

    /// <summary>Finalizes recording without submitting or waiting.</summary>
    void Finish();
}
