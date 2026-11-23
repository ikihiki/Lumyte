namespace Lumyte.UI.Collections;

/// <summary>Opt-in uniform item geometry with bounded overscan.</summary>
public sealed record UiItemVirtualization
{
    /// <summary>Initializes a new instance of the <see cref="UiItemVirtualization"/> class.</summary>
    /// <param name="itemExtent">The finite positive uniform logical item length.</param>
    /// <param name="overscan">Extra items on each side, between zero and 1024.</param>
    public UiItemVirtualization(float itemExtent, int overscan = 2)
    {
        if (!float.IsFinite(itemExtent) || itemExtent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemExtent));
        }

        if (overscan is < 0 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(overscan));
        }

        ItemExtent = itemExtent;
        Overscan = overscan;
    }

    /// <summary>Gets the uniform finite positive logical item length.</summary>
    public float ItemExtent { get; }

    /// <summary>Gets the maximum extra items on either side.</summary>
    public int Overscan { get; }

    internal float Extent(int count)
    {
        double extent = (double)count * ItemExtent;
        if (extent > float.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "The content extent must be representable as a finite float.");
        }

        return (float)extent;
    }

    internal (int Start, int End) Range(int count, float offset, float viewport)
    {
        if (viewport <= 0 || count == 0)
        {
            return (0, 0);
        }

        double first = Math.Floor((double)offset / ItemExtent);
        double end = Math.Ceiling(((double)offset + viewport) / ItemExtent);
        return ((int)Math.Clamp(first - Overscan, 0, count), (int)Math.Clamp(end + Overscan, 0, count));
    }
}
