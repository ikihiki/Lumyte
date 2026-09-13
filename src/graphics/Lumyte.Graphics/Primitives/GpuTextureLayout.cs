namespace Lumyte.Graphics;

/// <summary>Caller-specified texture layouts for backends with explicit transitions.</summary>
public enum GpuTextureLayout
{
    None,
    Undefined,
    General,
    ShaderRead,
    ColorAttachment,
    DepthStencilRead,
    DepthStencilWrite,
    CopySource,
    CopyDestination,
    Present,
    /// <summary>Queue-independent common layout for explicit transfers between graphics and copy queues.</summary>
    Common,
}
