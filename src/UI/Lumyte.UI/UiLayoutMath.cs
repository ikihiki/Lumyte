using System.Numerics;

namespace Lumyte.UI;

internal static class UiLayoutMath
{
    internal static void ValidateDimension(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    internal static void ValidateSize(Vector2 size, string parameterName)
    {
        ValidateDimension(size.X, parameterName);
        ValidateDimension(size.Y, parameterName);
    }

    internal static float FiniteFloat(double value)
    {
        if (!double.IsFinite(value) || value < -float.MaxValue || value > float.MaxValue)
        {
            throw new InvalidOperationException("The layout result cannot be represented by a finite float.");
        }

        return (float)value;
    }

    internal static Vector2 FillBounded(UiBoxConstraints constraints, double width, double height) => new(
        constraints.HasBoundedWidth ? constraints.MaxWidth : FiniteFloat(Math.Max(constraints.MinWidth, width)),
        constraints.HasBoundedHeight ? constraints.MaxHeight : FiniteFloat(Math.Max(constraints.MinHeight, height)));
}
