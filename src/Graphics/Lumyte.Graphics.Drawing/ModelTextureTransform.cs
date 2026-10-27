using System.Numerics;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines the offset, scale and rotation of one material texture, matching KHR_texture_transform.</summary>
/// <param name="Offset">The offset applied after scaling and rotation.</param>
/// <param name="Scale">The texture coordinate scale.</param>
/// <param name="Rotation">The rotation in radians.</param>
public readonly record struct ModelTextureTransform(Vector2 Offset, Vector2 Scale, float Rotation)
{
    /// <summary>Gets the unchanged coordinate transform.</summary>
    public static ModelTextureTransform Identity => new(Vector2.Zero, Vector2.One, 0);
}
