namespace Lumyte.Graphics.Drawing2D;

/// <summary>Specifies the distance channels of a linear, positive-inside distance field.</summary>
public enum DistanceFieldMode2D
{
    /// <summary>Uses the median of RGB pseudo-distance channels to preserve sharp corners.</summary>
    Msdf,

    /// <summary>Uses RGB for sharp fill and blends towards the alpha true-distance channel when minifying.</summary>
    Mtsdf,
}
