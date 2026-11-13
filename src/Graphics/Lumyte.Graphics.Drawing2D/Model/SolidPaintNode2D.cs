using Lumyte.Mathematics.Colors;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable solid paint node.</summary>
/// <param name="Color">The unit straight-alpha sRGB color.</param>
public sealed record SolidPaintNode2D(SrgbRgba<float> Color) : PaintNode2D
{
    private readonly SrgbRgba<float> _color = ValidateColor(Color);

    /// <summary>Gets or initializes the unit straight-alpha sRGB color.</summary>
    public SrgbRgba<float> Color { get => _color; init => _color = ValidateColor(value); }

    private static SrgbRgba<float> ValidateColor(SrgbRgba<float> value)
    {
        ModelValidation2D.Color(value, nameof(Color));
        return value;
    }
}
