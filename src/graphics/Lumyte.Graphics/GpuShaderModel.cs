namespace Lumyte.Graphics;

public enum GpuShaderCodeFormat : byte
{
    SpirV = 1,
    Dxil = 2,
    Wgsl = 3,
}

public enum GpuShaderStage : byte
{
    Vertex = 1,
    Pixel = 2,
    Compute = 3,
    Mesh = 4,
}
