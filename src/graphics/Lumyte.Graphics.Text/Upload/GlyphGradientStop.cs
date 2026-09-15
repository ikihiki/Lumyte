using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>A resolved color stop or a reference to the DrawText foreground brush, with a fixed alpha multiplier.</summary>
public readonly record struct GlyphGradientStop
{
    public GlyphGradientStop(float offset, Color color)
    {
        if (!float.IsFinite(offset))
        { throw new ArgumentOutOfRangeException(nameof(offset)); }
        Offset = offset;
        Color = color.Validate();
        IsForeground = false;
        ForegroundAlpha = 0;
    }
    private GlyphGradientStop(float offset, float alpha)
    {
        if (!float.IsFinite(offset))
        { throw new ArgumentOutOfRangeException(nameof(offset)); }
        if (!float.IsFinite(alpha) || alpha is < 0 or > 1)
        { throw new ArgumentOutOfRangeException(nameof(alpha)); }
        Offset = offset;
        Color = default;
        IsForeground = true;
        ForegroundAlpha = alpha;
    }
    public static GlyphGradientStop Foreground(float offset, float alpha = 1) => new(offset, alpha);
    public float Offset { get; }
    public Color Color { get; }
    public bool IsForeground { get; }
    public float ForegroundAlpha { get; }
}
