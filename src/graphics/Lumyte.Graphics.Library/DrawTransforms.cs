using System.Numerics;

namespace Lumyte.Graphics.Library;

public readonly record struct DrawTransforms(Matrix4x4 World, Matrix4x4 ViewProjection)
{
    public static DrawTransforms Identity => new(Matrix4x4.Identity, Matrix4x4.Identity);
}
