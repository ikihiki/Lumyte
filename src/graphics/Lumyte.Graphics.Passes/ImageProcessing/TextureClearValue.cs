using System.Numerics;

namespace Lumyte.Graphics.Passes;

/// <summary>A linear straight-alpha color or a depth/stencil clear value.</summary>
public readonly record struct TextureClearValue
{
    private TextureClearValue(bool isDepthStencil, Vector4 colorValue, float depth, uint stencil)
    {
        IsDepthStencil = isDepthStencil;
        ColorValue = colorValue;
        Depth = depth;
        Stencil = stencil;
    }

    public bool IsDepthStencil { get; }
    public Vector4 ColorValue { get; }
    public float Depth { get; }
    public uint Stencil { get; }

    public static TextureClearValue Color(Vector4 value) => new(false, value, 0, 0);
    public static TextureClearValue DepthStencil(float depth, uint stencil) => new(true, default, depth, stencil);
}
