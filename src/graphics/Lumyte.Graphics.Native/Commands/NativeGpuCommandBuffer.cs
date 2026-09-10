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
