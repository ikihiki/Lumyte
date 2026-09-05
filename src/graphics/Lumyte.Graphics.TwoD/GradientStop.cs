namespace Lumyte.Graphics.TwoD;

/// <summary>Associates a position on a gradient color line with a color.</summary>
public readonly record struct GradientStop(float Offset, Color Color)
{
    internal GradientStop Validate(string parameterName)
    {
        if (!float.IsFinite(Offset))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Gradient-stop offsets must be finite.");
        }
        Color.Validate();
        return this;
    }
}
