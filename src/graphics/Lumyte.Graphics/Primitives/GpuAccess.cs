namespace Lumyte.Graphics;

/// <summary>Memory access scopes for explicit GPU dependencies.</summary>
[Flags]
public enum GpuAccess : uint
{
    None = 0,
    ShaderRead = 1 << 0,
    ShaderWrite = 1 << 1,
    DescriptorRead = 1 << 2,
    ColorRead = 1 << 3,
    ColorWrite = 1 << 4,
    DepthStencilRead = 1 << 5,
    DepthStencilWrite = 1 << 6,
    CopyRead = 1 << 7,
    CopyWrite = 1 << 8,
    IndexRead = 1 << 9,
    IndirectRead = 1 << 10,
    HostRead = 1 << 11,
    HostWrite = 1 << 12,
}
