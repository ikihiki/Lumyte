using System.Numerics;

namespace Lumyte.Graphics.TwoD;

/// <summary>Describes a filtered shadow generated from an isolated layer's alpha.</summary>
public readonly record struct ShadowOptions(
    Vector2 Offset,
    Color Color,
    float BlurRadius = 0)
{
    internal ShadowOptions Validate(string parameterName)
    {
        if (!float.IsFinite(Offset.X) || !float.IsFinite(Offset.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Shadow offset must be finite.");
        }
        Color.Validate();
        if (!float.IsFinite(BlurRadius) || BlurRadius < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Shadow blur radius cannot be negative.");
        }
        return this;
    }
}
