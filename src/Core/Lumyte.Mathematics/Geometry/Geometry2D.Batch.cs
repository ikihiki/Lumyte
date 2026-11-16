using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>Provides two-dimensional geometry operations with explicit numeric and boundary policies.</summary>
public static partial class Geometry2D
{
    /// <summary>Computes squared distances to a fixed point, independently for each coordinate pair.</summary>
    /// <param name="x">The X coordinates.</param>
    /// <param name="y">The Y coordinates; its length must equal X.</param>
    /// <param name="pointX">The fixed point X coordinate.</param>
    /// <param name="pointY">The fixed point Y coordinate.</param>
    /// <param name="destination">Output; must contain at least X length elements.</param>
    /// <remarks>
    /// Evaluates dx = x[i] - pointX, dy = y[i] - pointY, then (dx * dx) + (dy * dy),
    /// with separate IEEE operations (no fused multiply-add). Infinity, overflow and NaN propagate;
    /// NaN payload bits are not specified. No cross-point reduction or reordered summation occurs.
    /// Destination may start at the same address as either input, but other overlap is rejected
    /// before writing. Only the first X length output elements are written. Empty input is valid.
    /// </remarks>
    /// <exception cref="ArgumentException">Lengths differ, destination is short, or overlap is shifted.</exception>
    public static void SquaredDistancesBcl(ReadOnlySpan<float> x, ReadOnlySpan<float> y, float pointX, float pointY, Span<float> destination)
    {
        Validate(x, y, destination);
        int i = 0;
        ref float sourceX = ref MemoryMarshal.GetReference(x);
        ref float sourceY = ref MemoryMarshal.GetReference(y);
        ref float target = ref MemoryMarshal.GetReference(destination);
        if (Vector256.IsHardwareAccelerated)
        {
            var px = Vector256.Create(pointX);
            var py = Vector256.Create(pointY);
            for (; i <= x.Length - Vector256<float>.Count; i += Vector256<float>.Count)
            {
                Vector256<float> dx = Vector256.LoadUnsafe(ref sourceX, (nuint)i) - px;
                Vector256<float> dy = Vector256.LoadUnsafe(ref sourceY, (nuint)i) - py;
                ((dx * dx) + (dy * dy)).StoreUnsafe(ref target, (nuint)i);
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            var px = Vector128.Create(pointX);
            var py = Vector128.Create(pointY);
            for (; i <= x.Length - Vector128<float>.Count; i += Vector128<float>.Count)
            {
                Vector128<float> dx = Vector128.LoadUnsafe(ref sourceX, (nuint)i) - px;
                Vector128<float> dy = Vector128.LoadUnsafe(ref sourceY, (nuint)i) - py;
                ((dx * dx) + (dy * dy)).StoreUnsafe(ref target, (nuint)i);
            }
        }

        for (; i < x.Length; i++)
        {
            float dx = x[i] - pointX;
            float dy = y[i] - pointY;
            destination[i] = (dx * dx) + (dy * dy);
        }
    }

    /// <summary>Computes squared distances to a fixed point, independently for each coordinate pair.</summary>
    /// <param name="x">The X coordinates.</param>
    /// <param name="y">The Y coordinates; its length must equal X.</param>
    /// <param name="pointX">The fixed point X coordinate.</param>
    /// <param name="pointY">The fixed point Y coordinate.</param>
    /// <param name="destination">Output; must contain at least X length elements.</param>
    /// <remarks>
    /// Evaluates dx = x[i] - pointX, dy = y[i] - pointY, then (dx * dx) + (dy * dy),
    /// with separate IEEE operations (no fused multiply-add). Infinity, overflow and NaN propagate;
    /// NaN payload bits are not specified. No cross-point reduction or reordered summation occurs.
    /// Destination may start at the same address as either input, but other overlap is rejected
    /// before writing. Only the first X length output elements are written. Empty input is valid.
    /// </remarks>
    /// <exception cref="ArgumentException">Lengths differ, destination is short, or overlap is shifted.</exception>
    public static void SquaredDistancesBcl(ReadOnlySpan<double> x, ReadOnlySpan<double> y, double pointX, double pointY, Span<double> destination)
    {
        Validate(x, y, destination);
        int i = 0;
        ref double sourceX = ref MemoryMarshal.GetReference(x);
        ref double sourceY = ref MemoryMarshal.GetReference(y);
        ref double target = ref MemoryMarshal.GetReference(destination);
        if (Vector256.IsHardwareAccelerated)
        {
            var px = Vector256.Create(pointX);
            var py = Vector256.Create(pointY);
            for (; i <= x.Length - Vector256<double>.Count; i += Vector256<double>.Count)
            {
                Vector256<double> dx = Vector256.LoadUnsafe(ref sourceX, (nuint)i) - px;
                Vector256<double> dy = Vector256.LoadUnsafe(ref sourceY, (nuint)i) - py;
                ((dx * dx) + (dy * dy)).StoreUnsafe(ref target, (nuint)i);
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            var px = Vector128.Create(pointX);
            var py = Vector128.Create(pointY);
            for (; i <= x.Length - Vector128<double>.Count; i += Vector128<double>.Count)
            {
                Vector128<double> dx = Vector128.LoadUnsafe(ref sourceX, (nuint)i) - px;
                Vector128<double> dy = Vector128.LoadUnsafe(ref sourceY, (nuint)i) - py;
                ((dx * dx) + (dy * dy)).StoreUnsafe(ref target, (nuint)i);
            }
        }

        for (; i < x.Length; i++)
        {
            double dx = x[i] - pointX;
            double dy = y[i] - pointY;
            destination[i] = (dx * dx) + (dy * dy);
        }
    }

    private static void Validate<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, Span<T> destination)
    {
        if (x.Length != y.Length || destination.Length < x.Length)
        {
            throw new ArgumentException("Coordinate lengths must match and destination must not be shorter.");
        }

        // Validate the written slice, not unused destination capacity.
        ReadOnlySpan<T> output = destination[..x.Length];
        if ((x.Overlaps(output, out int xOffset) && xOffset != 0)
            || (y.Overlaps(output, out int yOffset) && yOffset != 0))
        {
            throw new ArgumentException("Destination must not have shifted overlap with either coordinate span.");
        }
    }
}
