namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies the compiled shader code format.</summary>
public enum ShaderTarget
{
    /// <summary>Uses the Wgsl representation.</summary>
    Wgsl = 0,

    /// <summary>Uses the SpirV representation.</summary>
    SpirV = 1,

    /// <summary>Uses the DirectX intermediate language representation.</summary>
    Dxil = 2,
}
