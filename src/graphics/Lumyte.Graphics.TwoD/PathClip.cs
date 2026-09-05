using System.Numerics;

namespace Lumyte.Graphics.TwoD;

public readonly record struct PathClip(
    PathGeometry Geometry,
    Matrix3x2 Transform,
    FillRule FillRule = FillRule.NonZero)
{
    internal PathClip Validate()
    {
        ArgumentNullException.ThrowIfNull(Geometry);
        if (Geometry.IsEmpty) { throw new ArgumentException("Clip path cannot be empty.", nameof(Geometry)); }
        if (!Enum.IsDefined(FillRule)) { throw new ArgumentOutOfRangeException(nameof(FillRule)); }
        if (!float.IsFinite(Transform.M11) || !float.IsFinite(Transform.M12)
            || !float.IsFinite(Transform.M21) || !float.IsFinite(Transform.M22)
            || !float.IsFinite(Transform.M31) || !float.IsFinite(Transform.M32))
        {
            throw new ArgumentOutOfRangeException(nameof(Transform));
        }
        return this;
    }
}
