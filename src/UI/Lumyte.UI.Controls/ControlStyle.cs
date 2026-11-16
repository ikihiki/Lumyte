using Lumyte.Mathematics.Colors;

namespace Lumyte.UI.Controls;

internal static class ControlStyle
{
    internal static SRgba<float> Validate(SRgba<float> value)
    {
        if (!float.IsFinite(value.R) || !float.IsFinite(value.G) || !float.IsFinite(value.B) || !float.IsFinite(value.A)
            || value.R < 0 || value.R > 1 || value.G < 0 || value.G > 1 || value.B < 0 || value.B > 1 || value.A < 0 || value.A > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return value;
    }
}
