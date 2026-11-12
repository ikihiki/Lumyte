using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

// Keep enclosure rounding separate from intersection and size-preserving operations.
internal static class RectangleRounding<T>
    where T : INumber<T>
{
    internal static T EnclosingExtent(T min, T max)
    {
        if (max < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Maximum must not precede minimum.");
        }

        T extent = GeometryScalar<T>.Checked(checked(max - min));
        T upper = GeometryScalar<T>.Checked(checked(min + extent));
        if (upper >= max)
        {
            return extent;
        }

        // Correctly rounded subtraction is at most half an extent ULP short.
        // Its immediate successor is above the exact difference, so monotone
        // correctly rounded addition cannot reconstruct below max.
        if (typeof(T) == typeof(double))
        {
            extent = (T)(object)double.BitIncrement((double)(object)extent);
        }
        else if (typeof(T) == typeof(float))
        {
            extent = (T)(object)float.BitIncrement((float)(object)extent);
        }
        else if (typeof(T) == typeof(Half))
        {
            extent = (T)(object)Half.BitIncrement((Half)(object)extent);
        }
        else
        {
            extent = DecimalSuccessor(extent);
        }

        GeometryScalar<T>.Checked(extent);
        upper = GeometryScalar<T>.Checked(checked(min + extent));
        if (upper < max)
        {
            throw new OverflowException("The selected scalar cannot represent a verified enclosing extent.");
        }

        return extent;
    }

    private static T DecimalSuccessor(T value)
    {
        decimal number;
        try
        {
            number = decimal.CreateChecked(value);
            if (T.CreateChecked(number) != value)
            {
                throw new OverflowException("The scalar has no faithful decimal representation.");
            }
        }
        catch (NotSupportedException exception)
        {
            throw new OverflowException("The scalar has no supported outward rounding operation.", exception);
        }

        int[] bits = decimal.GetBits(number);
        BigInteger coefficient = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
        int scale = (bits[3] >> 16) & 255;
        BigInteger limit = (BigInteger.One << 96) - 1;

        // Normalize to the finest possible decimal grid, irrespective of the
        // input's stored scale. At most 28 refinements and 28 coarsenings.
        while (scale < 28 && (coefficient * 10) + 1 <= limit)
        {
            coefficient *= 10;
            scale++;
        }

        while (coefficient >= limit && scale > 0)
        {
            coefficient /= 10;
            scale--;
        }

        coefficient++;
        if (coefficient > limit)
        {
            throw new OverflowException("No finite decimal successor exists.");
        }

        decimal successor = new((int)(uint)(coefficient & uint.MaxValue), (int)(uint)((coefficient >> 32) & uint.MaxValue), (int)(uint)((coefficient >> 64) & uint.MaxValue), false, (byte)scale);
        T result = T.CreateChecked(successor);
        if (result <= value || decimal.CreateChecked(result) != successor)
        {
            throw new OverflowException("The scalar cannot faithfully represent the decimal successor.");
        }

        return result;
    }
}
