namespace Lumyte.Graphics;

/// <summary>Execution scopes for explicit GPU dependencies.</summary>
[Flags]
public enum GpuStage : uint
{
    None = 0,
    DrawIndirect = 1 << 0,
    VertexShader = 1 << 1,
    PixelShader = 1 << 2,
    ComputeShader = 1 << 3,
    ColorOutput = 1 << 4,
    DepthStencil = 1 << 5,
    Copy = 1 << 6,
    AllGraphics = 1 << 7,
    All = 1 << 8,
    IndexInput = 1 << 9,
    AmplificationShader = 1 << 10,
    MeshShader = 1 << 11,
    Host = 1 << 12,
}
