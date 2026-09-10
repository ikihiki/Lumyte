namespace Lumyte.Graphics.Native;

/// <summary>Attachment aspects that remain read-only when using a render view.</summary>
[Flags]
public enum NativeGpuRenderViewFlags
{
    None = 0,
    DepthReadOnly = 1 << 0,
    StencilReadOnly = 1 << 1,
}
