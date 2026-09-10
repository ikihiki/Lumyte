namespace Lumyte.Graphics.Native;

/// <summary>The selected components of a texture. Copies select one aspect at a time.</summary>
public enum NativeGpuTextureAspect
{
    Color,
    Depth,
    Stencil,
    DepthStencil,
}
