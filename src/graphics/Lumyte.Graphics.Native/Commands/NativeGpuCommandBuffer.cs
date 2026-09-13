namespace Lumyte.Graphics.Native;

/// <summary>A one-shot recording that borrows application resources from the caller.</summary>
/// <remarks>
/// The caller serializes recording, submission, and disposal of this object and keeps referenced
/// resources alive until GPU completion. Submitted work is never cancelled or waited on by Dispose.
/// Backend-owned command memory remains alive until completion even if this object is disposed.
/// The caller disposes all recordings before disposing their backend, including unsubmitted recordings.
/// </remarks>
public abstract class NativeGpuCommandBuffer : IDisposable
{
    protected NativeGpuCommandBuffer() { }

    /// <summary>Selects the complete resource heap without enumerating or retaining its referenced resources.</summary>
    public abstract void SetResourceDescriptorHeap(NativeGpuDescriptorHeap heap);

    /// <summary>Selects the independent sampler heap. The caller keeps the heap alive until GPU completion.</summary>
    public abstract void SetSamplerDescriptorHeap(NativeGpuDescriptorHeap heap);

    public abstract void SetPipeline(NativeGpuRasterPipelineHandle pipeline);

    public abstract void SetDepthStencilState(NativeGpuDepthStencilState state);

    public abstract void SetViewport(NativeGpuViewport viewport);

    public abstract void SetScissor(NativeGpuScissorRect scissor);

    /// <summary>Begins rendering using borrowed views and snapshots the attachment values before returning.</summary>
    /// <remarks>
    /// At least one attachment is required. Initial viewport/scissor cover the first color view's mip,
    /// or the depth/stencil view's mip when no color view is supplied. Other attachments must cover that extent.
    /// Depth/stencil tests and writes are disabled at the beginning of each rendering scope.
    /// </remarks>
    public abstract void BeginRendering(ReadOnlySpan<NativeGpuColorAttachment> colorAttachments,
        NativeGpuDepthStencilAttachment? depthStencilAttachment = null);

    public abstract void EndRendering();

    /// <summary>Records a draw with direct root bytes, copied before the call returns.</summary>
    /// <remarks>The caller supplies all root bytes read by this work; trailing bytes are not zero-filled.</remarks>
    public abstract void Draw(ReadOnlySpan<byte> rootData, uint vertexCount, uint instanceCount = 1,
        uint firstVertex = 0, uint firstInstance = 0);

    /// <summary>Uses native index fetch from the caller's region-relative range and direct root bytes.</summary>
    public abstract void DrawIndexed(ReadOnlySpan<byte> rootData, NativeGpuRange indices, NativeGpuIndexFormat format,
        uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0);

    /// <summary>Draws once from vertexCount, instanceCount, firstVertex, firstInstance uint32 values at the beginning of the argument range.</summary>
    /// <remarks>Root bytes follow the same direct-input snapshot contract as Draw.</remarks>
    public abstract void DrawIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments);

    /// <summary>Draws once from indexCount, instanceCount, firstIndex, baseVertex, firstInstance at the beginning of the argument range.</summary>
    /// <remarks>Fields are 32-bit values, with only baseVertex signed. Root bytes remain direct inputs with the same snapshot contract as Draw.</remarks>
    public abstract void DrawIndexedIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange indices,
        NativeGpuIndexFormat format, NativeGpuRange arguments);

    /// <summary>Selects a borrowed pipeline that the caller keeps alive through recorded and submitted work.</summary>
    public abstract void SetComputePipeline(NativeGpuComputePipelineHandle pipeline);

    /// <summary>Copies root bytes directly into this work's shader arguments. The source span may be reused after return.</summary>
    /// <remarks>The caller supplies all root bytes read by this work; trailing bytes are not zero-filled.</remarks>
    public abstract void Dispatch(ReadOnlySpan<byte> rootData, uint x, uint y = 1, uint z = 1);

    /// <summary>Dispatches once using three uint group counts at the start of the caller's range and direct root bytes.</summary>
    /// <remarks>Root bytes follow the same snapshot and lifetime contract as direct dispatch.</remarks>
    public abstract void DispatchIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments);

    /// <summary>Copies source bytes using region-relative offsets. The destination must cover the source size.</summary>
    public abstract void CopyMemory(NativeGpuRange source, NativeGpuRange destination);

    public abstract void CopyMemoryToTexture(
        NativeGpuRange source, NativeGpuTextureHandle destination, NativeGpuTextureCopyFootprint footprint);

    public abstract void CopyTextureToMemory(
        NativeGpuTextureHandle source, NativeGpuRange destination, NativeGpuTextureCopyFootprint footprint);

    /// <summary>Records a global dependency without enumerating resources or inferring hazards.</summary>
    public abstract void Barrier(
        GpuStage beforeStages, GpuAccess beforeAccess, GpuStage afterStages, GpuAccess afterAccess);

    /// <summary>Records the caller's explicit layout change where the backend supports this operation.</summary>
    public abstract void TextureTransition(
        NativeGpuTextureView view, GpuTextureLayout beforeLayout, GpuTextureLayout afterLayout);

    /// <summary>Discards the selected subresources and initializes their layout without resolving alias dependencies.</summary>
    public abstract void DiscardTexture(NativeGpuTextureView view, GpuTextureLayout afterLayout);

    public abstract void Dispose();
}
