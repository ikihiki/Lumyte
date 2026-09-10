namespace Lumyte.Graphics.Native;

/// <summary>
/// Native GPU allocation and linear placement. The caller owns each resource and its backing heap.
/// Shader, texture, recording, and submission members are added in subsequent implementation stages.
/// </summary>
/// <remarks>
/// The caller serializes operations on the same heap or region, including placement and destruction,
/// and does not dispose the backend concurrently with its use. GPU synchronization is also caller-owned.
/// Implementations in any assembly derive their resource types from the public heap, region, and
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
}
