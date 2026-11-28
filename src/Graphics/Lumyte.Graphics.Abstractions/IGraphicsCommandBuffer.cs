namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns one-shot GPU command memory; the caller manages synchronization.</summary>
/// <remarks>The caller keeps referenced resources and command memory alive until GPU use completes; submission does not rescan their lifetimes.</remarks>
public interface IGraphicsCommandBuffer : IDisposable
{
    /// <summary>Gets the recording and execution state.</summary>
    CommandBufferState State { get; }

    /// <summary>Materializes a command-local render variant and immutable arguments before recording.</summary>
    /// <typeparam name="T">The immutable shader argument type.</typeparam>
    /// <param name="pass">The render attachments.</param>
    /// <param name="pipeline">The graphics program.</param>
    /// <param name="state">The immutable render state.</param>
    /// <param name="table">The argument table, if any.</param>
    /// <param name="arguments">Arguments with a stable non-null preparation identity.</param>
    /// <param name="indexFormat">The index format, or null for nonindexed execution.</param>
    void PrepareRender<T>(RenderPassDesc pass, IGraphicsPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments, IndexFormat? indexFormat = null)
        where T : struct, IShaderArguments => throw new NotSupportedException("Native preparation is not implemented.");

    /// <summary>Materializes a command-local compute variant and immutable arguments before recording.</summary>
    /// <typeparam name="T">The immutable shader argument type.</typeparam>
    /// <param name="pipeline">The compute program.</param>
    /// <param name="table">The argument table, if any.</param>
    /// <param name="arguments">Arguments with a stable non-null preparation identity.</param>
    void PrepareCompute<T>(IGraphicsComputePipeline pipeline, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments => throw new NotSupportedException("Native preparation is not implemented.");

    /// <summary>Materializes a command-local mesh variant and immutable arguments before recording.</summary>
    /// <typeparam name="T">The immutable shader argument type.</typeparam>
    /// <param name="pass">The render attachments.</param>
    /// <param name="pipeline">The mesh program.</param>
    /// <param name="state">The immutable render state.</param>
    /// <param name="table">The argument table, if any.</param>
    /// <param name="arguments">Arguments with a stable non-null preparation identity.</param>
    void PrepareMesh<T>(RenderPassDesc pass, IMeshPipeline pipeline, GraphicsRenderStateDesc state, IArgumentTable? table, in T arguments)
        where T : struct, IShaderArguments => throw new NotSupportedException("Native preparation is not implemented.");

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

    /// <summary>Records an explicit upload-staging to GPU shader data copy.</summary>
    /// <typeparam name="T">The application shader data structure.</typeparam>
    /// <param name="source">The unmapped upload staging range.</param>
    /// <param name="destination">The compatible GPU destination range.</param>
    void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
        where T : struct, IShaderArguments;

    /// <summary>Records a dependency for a shader data range.</summary>
    /// <typeparam name="T">The application shader data structure.</typeparam>
    /// <param name="barrier">The explicit dependency.</param>
    void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
        where T : struct, IShaderArguments;
}
