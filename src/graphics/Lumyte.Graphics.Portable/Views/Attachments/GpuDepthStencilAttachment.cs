namespace Lumyte.Graphics.Portable;

/// <summary>A non-owning attachment with independent depth and stencil operations.</summary>
/// <remarks>Read-only and absent aspects omit their load/store operations. The runtime validates attachment compatibility.</remarks>
public readonly record struct GpuDepthStencilAttachment(
    GpuTextureView View,
    bool DepthReadOnly = false,
    bool StencilReadOnly = false,
    GpuAttachmentLoadOperation? DepthLoadOperation = null,
    GpuAttachmentStoreOperation? DepthStoreOperation = null,
    GpuAttachmentLoadOperation? StencilLoadOperation = null,
    GpuAttachmentStoreOperation? StencilStoreOperation = null,
    GpuClearDepthStencil ClearValue = default);
