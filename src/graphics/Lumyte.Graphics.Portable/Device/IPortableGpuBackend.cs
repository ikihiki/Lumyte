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

    /// <summary>Consumes the entry span during this call and creates an immutable group layout.</summary>
    GpuBindingLayoutHandle CreateBindingLayout(ReadOnlySpan<GpuBindingLayoutEntry> entries);
    /// <summary>Destroys the layout after its bindings, programs, pipelines and recorded uses have ended.</summary>
    void DestroyBindingLayout(GpuBindingLayoutHandle layout);
    /// <summary>Snapshots input values and owns internal binding objects, without owning application resources.</summary>
    GpuBindingsHandle CreateBindings(GpuBindingLayoutHandle layout, ReadOnlySpan<GpuBindingEntry> entries);
    /// <summary>Releases internal objects after all uses end, without destroying referenced resources.</summary>
    void DestroyBindings(GpuBindingsHandle bindings);
}
