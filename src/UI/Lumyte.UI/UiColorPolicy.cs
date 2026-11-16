using Lumyte.Mathematics.Colors;

namespace Lumyte.UI;

internal static class UiColorPolicy
{
    internal static T Value<T>(T value, string parameterName)
    {
        if ((typeof(T) == typeof(SRgba<float>) || typeof(T) == typeof(SRgba<float>?)) && value is SRgba<float> color)
        {
            Unit(color, parameterName);
        }

        return value;
    }

    internal static SRgba<float> Unit(SRgba<float> color, string parameterName)
    {
        Validate(color.R, color.G, color.B, color.A, parameterName);
        return color;
    }

    internal static void Validate(float r, float g, float b, float a, string parameterName)
    {
        if (!float.IsFinite(r) || r < 0 || r > 1
            || !float.IsFinite(g) || g < 0 || g > 1
            || !float.IsFinite(b) || b < 0 || b > 1
            || !float.IsFinite(a) || a < 0 || a > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
