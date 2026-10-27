using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text;

/// <summary>An immutable CPU-generated RGBA8 distance field with a logical placement rectangle.</summary>
public sealed class DistanceFieldImage2D
{
    internal DistanceFieldImage2D(int width, int height, Rect2D planeBounds, float pixelsPerUnit, float distanceRange, DistanceFieldMode2D mode, byte[] pixels)
    {
        Width = width;
        Height = height;
        PlaneBounds = planeBounds;
        PixelsPerUnit = pixelsPerUnit;
        DistanceRange = distanceRange;
        Mode = mode;
        Pixels = pixels;
    }

    /// <summary>Gets the width in texels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in texels.</summary>
    public int Height { get; }

    /// <summary>Gets the top-left logical rectangle, including the distance-field padding.</summary>
    public Rect2D PlaneBounds { get; }

    /// <summary>Gets the number of source texels per logical unit.</summary>
    public float PixelsPerUnit { get; }

    /// <summary>Gets the full encoded distance range in source texels, with zero at 0.5 and positive values inside.</summary>
    public float DistanceRange { get; }

    /// <summary>Gets the multi-channel field representation.</summary>
    public DistanceFieldMode2D Mode { get; }

    /// <summary>Gets tightly packed RGBA8 linear data, with the first row at the logical top.</summary>
    public ReadOnlyMemory<byte> Pixels { get; }
}
