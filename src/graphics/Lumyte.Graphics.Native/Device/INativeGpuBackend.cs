namespace Lumyte.Graphics.Native;

/// <summary>
/// Native GPU allocation and explicit linear or texture placement. The caller owns each resource and its backing heap.
/// Shader, recording, and submission members are added in subsequent implementation stages.
/// </summary>
/// <remarks>
/// The caller serializes operations on the same heap or resource, including placement and destruction,
/// and does not dispose the backend concurrently with its use. GPU synchronization is also caller-owned.
/// Implementations in any assembly derive their resource types from the public heap, region, texture, and
/// compatibility bases. Each backend retains and checks resource ownership in its own private types.
/// </remarks>
public interface INativeGpuBackend : IDisposable
{
    GpuShaderCodeFormat ShaderCodeFormat { get; }
    NativeGpuCapabilities Capabilities { get; }

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
}
