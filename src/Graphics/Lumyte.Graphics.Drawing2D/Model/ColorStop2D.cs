using Lumyte.Mathematics.Colors;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>A gradient stop; duplicate offsets preserve their insertion order.</summary>
/// <param name="Offset">The finite stop offset, not restricted to zero through one.</param>
/// <param name="Color">The unit straight-alpha sRGB color.</param>
public readonly record struct ColorStop2D(float Offset, Rgba<float> Color)
{
    private readonly float _offset = ValidateOffset(Offset);
    private readonly Rgba<float> _color = ValidateColor(Color);

    /// <summary>Gets or initializes the finite offset, including values outside the unit interval.</summary>
    public float Offset { get => _offset; init => _offset = ValidateOffset(value); }

    /// <summary>Gets or initializes the unit straight-alpha sRGB color.</summary>
    public Rgba<float> Color { get => _color; init => _color = ValidateColor(value); }

    private static float ValidateOffset(float value)
    {
        ModelValidation2D.Finite(value, nameof(Offset));
        return value;
    }

    private static Rgba<float> ValidateColor(Rgba<float> value)
    {
        ModelValidation2D.Color(value, nameof(Color));
        return value;
    }
}
