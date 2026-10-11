namespace Lumyte.Graphics.Drawing;

/// <summary>Identifies material sampling semantics, including the required texture color space.</summary>
public enum ModelTextureRole
{
    /// <summary>sRGB base color with linear straight alpha.</summary>
    BaseColor,

    /// <summary>Linear blue metallic and green roughness channels.</summary>
    MetallicRoughness,

    /// <summary>Linear tangent-space normal channels.</summary>
    Normal,

    /// <summary>Linear red occlusion channel.</summary>
    Occlusion,

    /// <summary>sRGB emissive color.</summary>
    Emissive,
}
