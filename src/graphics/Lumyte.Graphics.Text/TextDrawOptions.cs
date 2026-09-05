using System.Numerics;

using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>Controls placement and route selection for one shaped text run.</summary>
public sealed record TextDrawOptions
{
    public TextRenderingMode RenderingMode { get; init; } = TextRenderingMode.Auto;

    /// <summary>Controls whether vector or bitmap colors embedded in the font are used.</summary>
    public ColorGlyphMode ColorGlyphMode { get; init; } = ColorGlyphMode.Auto;

    /// <summary>The zero-based CPAL palette selected for layered vector color glyphs.</summary>
    public uint ColorPaletteIndex { get; init; }

    /// <summary>A local-to-target transform applied around the supplied text coordinates.</summary>
    public Matrix3x2 Transform { get; init; } = Matrix3x2.Identity;

    /// <summary>Additional logical-to-physical scale, normally the display scale factor.</summary>
    public float DeviceScale { get; init; } = 1;

    public FillRule FillRule { get; init; } = FillRule.NonZero;
    public float DistanceRange { get; init; } = 4;

    internal void Validate()
    {
        if (!Enum.IsDefined(RenderingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(RenderingMode));
        }
        if (!Enum.IsDefined(ColorGlyphMode))
        {
            throw new ArgumentOutOfRangeException(nameof(ColorGlyphMode));
        }
        if (!Enum.IsDefined(FillRule))
        {
            throw new ArgumentOutOfRangeException(nameof(FillRule));
        }
        if (!float.IsFinite(DeviceScale) || DeviceScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DeviceScale));
        }
        if (!float.IsFinite(DistanceRange) || DistanceRange <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DistanceRange));
        }
        if (!float.IsFinite(Transform.M11) || !float.IsFinite(Transform.M12)
            || !float.IsFinite(Transform.M21) || !float.IsFinite(Transform.M22)
            || !float.IsFinite(Transform.M31) || !float.IsFinite(Transform.M32))
        {
            throw new ArgumentOutOfRangeException(nameof(Transform));
        }
    }
}
