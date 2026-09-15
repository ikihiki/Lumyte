namespace Lumyte.Graphics;

/// <summary>Shared texture format names. Each backend validates its supported uses.</summary>
public enum GpuFormat
{
    Rgba8Unorm,
    Bgra8Unorm,
    R32Float,
    D32Float,
    Rgba8UnormSrgb,
    Bgra8UnormSrgb,
    R8Unorm,
    Rg8Unorm,
    Depth24PlusStencil8,
    Rgba16Float,
}
