namespace Lumyte.Graphics.Portable;

/// <summary>An independent Portable device with resources that own their backing memory.</summary>
/// <remarks>
/// Callers own resource lifetimes and serialize operations on the same resource.
/// Mapping and mapped memory access must finish before destruction; backend disposal must not race with use.
/// Resources are not retained by copying handles. Disposal does not search for application resources or wait for GPU work.
/// External backends implement this interface and derive their private handles from public/protected contracts.
/// </remarks>
public interface IPortableGpuBackend : IDisposable
{
    GpuBackendCapabilities Capabilities { get; }
    GpuDeviceLimits Limits { get; }
    GpuBufferHandle CreateBuffer(GpuBufferDescription description);
    void DestroyBuffer(GpuBufferHandle buffer);
    ValueTask<GpuMappedBufferRange> MapBufferAsync(GpuBufferHandle buffer, GpuMapMode mode, ulong offset, ulong length);
    GpuTextureHandle CreateTexture(GpuTextureDescription description);
    void DestroyTexture(GpuTextureHandle texture);
}
