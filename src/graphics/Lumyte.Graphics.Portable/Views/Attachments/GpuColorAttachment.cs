namespace Lumyte.Graphics.Portable;

/// <summary>A non-owning color attachment declaration. The caller retains the texture through all uses.</summary>
public readonly record struct GpuColorAttachment(
    GpuTextureView View,
    GpuAttachmentLoadOperation LoadOperation = GpuAttachmentLoadOperation.Load,
    GpuAttachmentStoreOperation StoreOperation = GpuAttachmentStoreOperation.Store,
    GpuClearColor ClearColor = default,
    GpuTextureView? ResolveTarget = null,
    uint? DepthSlice = null);
