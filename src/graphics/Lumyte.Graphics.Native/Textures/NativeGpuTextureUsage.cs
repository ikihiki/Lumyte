namespace Lumyte.Graphics.Native;

[Flags]
public enum NativeGpuTextureUsage
{
    None = 0,
    Sampled = 1 << 0,
    Storage = 1 << 1,
    ColorAttachment = 1 << 2,
    DepthStencilAttachment = 1 << 3,
    CopySource = 1 << 4,
    CopyDestination = 1 << 5,
}
