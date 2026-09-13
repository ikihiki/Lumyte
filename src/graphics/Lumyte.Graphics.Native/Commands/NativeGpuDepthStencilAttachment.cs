namespace Lumyte.Graphics.Native;

/// <summary>Per-aspect attachment operations. Absent and read-only aspects leave their operations unspecified.</summary>
/// <remarks>Read-only properties derive from the view so the attachment cannot disagree with its native view flags.</remarks>
public readonly record struct NativeGpuDepthStencilAttachment(
    NativeGpuRenderViewHandle View,
    NativeGpuLoadOp? DepthLoadOp = null,
    NativeGpuStoreOp? DepthStoreOp = null,
    NativeGpuLoadOp? StencilLoadOp = null,
    NativeGpuStoreOp? StencilStoreOp = null,
    float ClearDepth = 1,
    byte ClearStencil = 0)
{
    public bool DepthReadOnly => (View.Flags & NativeGpuRenderViewFlags.DepthReadOnly) != 0;
    public bool StencilReadOnly => (View.Flags & NativeGpuRenderViewFlags.StencilReadOnly) != 0;
}
