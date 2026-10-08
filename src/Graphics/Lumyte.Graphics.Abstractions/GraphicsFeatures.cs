namespace Lumyte.Graphics.Abstractions;

/// <summary>Identifies portable graphics capabilities independently of operation APIs.</summary>
[Flags]
public enum GraphicsFeatures
{
    /// <summary>Indicates no reported capabilities.</summary>
    None = 0,

    /// <summary>Indicates support for reading draw arguments from a GPU buffer.</summary>
    IndirectDraw = 1,

    /// <summary>Indicates support for anisotropic texture sampling.</summary>
    AnisotropicFiltering = 2,

    /// <summary>Indicates support for clamping rasterizer depth bias.</summary>
    DepthBiasClamp = 4,

    /// <summary>Indicates support for mesh shader stages.</summary>
    MeshShader = 8,
}
