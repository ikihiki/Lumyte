using System.Numerics;

namespace Lumyte.Graphics.Text;

/// <summary>Maps effective on-screen font sizes to the available 2D glyph routes.</summary>
public sealed record TextRenderingPolicy
{
    public static TextRenderingPolicy Default { get; } = new();

    public float CoverageMaximumSize { get; init; } = 18;
    public float SignedDistanceMaximumSize { get; init; } = 48;
    public float MultiChannelSignedDistanceMaximumSize { get; init; } = 96;
    public float PolygonMaximumSize { get; init; } = 256;

    public TextRenderingMode Select(float fontSize)
        => Select(fontSize, Matrix3x2.Identity, 1);

    /// <summary>
    /// Selects a route from the logical font size, text transform, and output device scale.
    /// The largest singular value is used for rotated, sheared, or non-uniform transforms.
    /// </summary>
    public TextRenderingMode Select(float fontSize, Matrix3x2 transform, float deviceScale = 1)
    {
        Validate();
        ValidatePositive(fontSize, nameof(fontSize));
        ValidatePositive(deviceScale, nameof(deviceScale));
        ValidateTransform(transform);

        float effectiveSize = checked(fontSize * deviceScale) * MaximumScale(transform);
        if (!float.IsFinite(effectiveSize))
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        if (effectiveSize <= CoverageMaximumSize) { return TextRenderingMode.Coverage; }
        if (effectiveSize <= SignedDistanceMaximumSize) { return TextRenderingMode.SignedDistance; }
        if (effectiveSize <= MultiChannelSignedDistanceMaximumSize)
        {
            return TextRenderingMode.MultiChannelSignedDistance;
        }
        if (effectiveSize <= PolygonMaximumSize) { return TextRenderingMode.Polygon; }
        return TextRenderingMode.VectorPath;
    }

    internal float EffectiveSize(float fontSize, Matrix3x2 transform, float deviceScale)
    {
        Validate();
        ValidatePositive(fontSize, nameof(fontSize));
        ValidatePositive(deviceScale, nameof(deviceScale));
        ValidateTransform(transform);
        float result = fontSize * deviceScale * MaximumScale(transform);
        return float.IsFinite(result)
            ? result
            : throw new ArgumentOutOfRangeException(nameof(fontSize));
    }

    internal void Validate()
    {
        ValidatePositive(CoverageMaximumSize, nameof(CoverageMaximumSize));
        ValidatePositive(SignedDistanceMaximumSize, nameof(SignedDistanceMaximumSize));
        ValidatePositive(MultiChannelSignedDistanceMaximumSize, nameof(MultiChannelSignedDistanceMaximumSize));
        ValidatePositive(PolygonMaximumSize, nameof(PolygonMaximumSize));
        if (CoverageMaximumSize > SignedDistanceMaximumSize
            || SignedDistanceMaximumSize > MultiChannelSignedDistanceMaximumSize
            || MultiChannelSignedDistanceMaximumSize > PolygonMaximumSize)
        {
            throw new InvalidOperationException("Text rendering size thresholds must be in ascending order.");
        }
    }

    private static float MaximumScale(Matrix3x2 transform)
    {
        float first = transform.M11 * transform.M11 + transform.M12 * transform.M12;
        float second = transform.M21 * transform.M21 + transform.M22 * transform.M22;
        float cross = transform.M11 * transform.M21 + transform.M12 * transform.M22;
        float discriminant = MathF.Sqrt(MathF.Max(
            0,
            (first - second) * (first - second) + 4 * cross * cross));
        return MathF.Sqrt(MathF.Max(0, (first + second + discriminant) * 0.5f));
    }

    private static void ValidatePositive(float value, string parameter)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }

    private static void ValidateTransform(Matrix3x2 transform)
    {
        if (!float.IsFinite(transform.M11) || !float.IsFinite(transform.M12)
            || !float.IsFinite(transform.M21) || !float.IsFinite(transform.M22)
            || !float.IsFinite(transform.M31) || !float.IsFinite(transform.M32))
        {
            throw new ArgumentOutOfRangeException(nameof(transform));
        }
    }
}
