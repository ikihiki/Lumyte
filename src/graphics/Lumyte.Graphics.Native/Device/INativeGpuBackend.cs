namespace Lumyte.Graphics.Native;

/// <summary>
/// Native GPU allocation, placement, views, descriptors, recording, and explicit submission and completion.
/// The caller owns each resource and its backing heap. Raster drawing members follow in later stages.
/// </summary>
/// <remarks>
/// The caller serializes operations on the same heap or resource, including placement and destruction,
/// and does not dispose the backend concurrently with its use. GPU synchronization is also caller-owned.
/// Before disposing the backend, the caller completes submitted work, disposes all recordings and semaphores,
/// and destroys application resources and heaps. Disposal does not perform an implicit GPU wait.
/// Implementations in any assembly derive their resource types from public or protected extension contracts.
/// Each backend retains and checks resource ownership in its own private types.
/// </remarks>
public interface INativeGpuBackend : IDisposable
{
    GpuShaderCodeFormat ShaderCodeFormat { get; }
    NativeGpuCapabilities Capabilities { get; }
    NativeGpuLimits Limits { get; }
    NativeGpuQueue MainQueue { get; }

    NativeGpuMemoryRequirements GetLinearMemoryRequirements(ulong size, NativeGpuMemoryKind kind);

    NativeGpuHeap CreateGpuHeap(
        ulong size,
        ulong alignment,
        NativeGpuMemoryKind kind,
        ReadOnlySpan<NativeGpuMemoryCompatibility> compatibilities);

    /// <summary>Releases the allocation after the caller has destroyed its placed resources.</summary>
    void DestroyGpuHeap(NativeGpuHeap heap);

    NativeGpuLinearRegion CreateLinearRegion(ulong size, NativeGpuHeap heap, ulong offset);

    /// <summary>Releases the linear resource and its mapping, without releasing its heap.</summary>
    void DestroyLinearRegion(NativeGpuLinearRegion region);

    NativeGpuMemoryRequirements GetTextureMemoryRequirements(
        NativeGpuTextureDescription description, NativeGpuMemoryKind kind);

    /// <summary>Places a texture with undefined contents in the caller's heap.</summary>
    NativeGpuTextureHandle CreateTexture(NativeGpuTextureDescription description, NativeGpuHeap heap, ulong offset);

    /// <summary>Releases the texture after all uses have ended, without releasing its heap.</summary>
    void DestroyTexture(NativeGpuTextureHandle texture);

    NativeGpuRenderViewHandle CreateRenderView(
        NativeGpuTextureView view, NativeGpuRenderViewFlags flags = NativeGpuRenderViewFlags.None);

    /// <summary>Releases the attachment view after GPU use, without releasing its texture.</summary>
    void DestroyRenderView(NativeGpuRenderViewHandle view);

    NativeGpuDescriptorHeap CreateDescriptorHeap(NativeGpuDescriptorHeapKind kind, uint capacity);

    /// <summary>Releases descriptor storage after all uses have ended, without releasing referenced resources.</summary>
    void DestroyDescriptorHeap(NativeGpuDescriptorHeap heap);

    /// <summary>Writes a caller-selected resource slot without initializing or retaining the texture.</summary>
    void WriteTextureDescriptor(NativeGpuDescriptorHeap heap, uint index,
        NativeGpuTextureView view, NativeGpuTextureDescriptorType type = NativeGpuTextureDescriptorType.Sampled);

    void WriteBufferDescriptor(NativeGpuDescriptorHeap heap, uint index,
        NativeGpuRange range, NativeGpuBufferAccess access);

    void WriteSamplerDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuSamplerDescription description);

    /// <summary>Completes native compute pipeline creation before returning; shader bytes are borrowed only during this call.</summary>
    NativeGpuComputePipelineHandle CreateComputePipeline(NativeGpuShaderProgram program);

    void DestroyComputePipeline(NativeGpuComputePipelineHandle pipeline);
}
