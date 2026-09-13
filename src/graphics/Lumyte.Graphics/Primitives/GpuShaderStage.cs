namespace Lumyte.Graphics;

/// <summary>One Native shader entry stage. Portable defines its own stage visibility flags.</summary>
public enum GpuShaderStage : byte
{
    Vertex = 1,
    Pixel = 2,
    Compute = 3,
    Mesh = 4,
    Amplification = 5,
}
