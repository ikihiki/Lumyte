namespace Lumyte.Mathematics;

/// <summary>Provides integer interpolation with exact binary weights.</summary>
public static class IntegerInterpolation
{
    /// <summary>Interpolates integer endpoints, rounding to the nearest integer with ties to even.</summary>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The ending value.</param>
    /// <param name="amount">The finite binary floating-point weight, allowing extrapolation.</param>
    /// <returns>The rounded value of <c>from + (to - from) * amount</c>, evaluated exactly before rounding.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The weight is not finite.</exception>
    /// <exception cref="OverflowException">The rounded value is outside the range of <see cref="long"/>.</exception>
    public static long Linear(long from, long to, float amount)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(float.IsFinite(amount), true, nameof(amount));
        if (amount == 0 || from == to)
        {
            return from;
        }

        if (amount == 1)
        {
            return to;
        }

        uint bits = BitConverter.SingleToUInt32Bits(amount);
        int exponent = (int)((bits >> 23) & 0xff);
        uint significand = bits & 0x7fffff;
        if (exponent == 0)
        {
            exponent = -149;
        }
        else
        {
            significand |= 1 << 23;
            exponent -= 150;
        }

        // The endpoint difference has at most 64 magnitude bits and the significand at most 24.
        Int128 product = ((Int128)to - from) * significand;
        if ((bits & 0x80000000) != 0)
        {
            product = -product;
        }

        if (exponent >= 0)
        {
            // A larger adjustment cannot be brought into the long range by either endpoint.
            Int128 limit = exponent < 64 ? (Int128)ulong.MaxValue >> exponent : 0;
            if (product < -limit || product > limit)
            {
                throw new OverflowException("The interpolated value is outside the integer range.");
            }

            return checked((long)((Int128)from + (product << exponent)));
        }

        int shift = -exponent;
        if (shift >= 89)
        {
            // An 88-bit product divided by at least 2^89 has magnitude strictly below one half.
            return from;
        }

        Int128 divisor = (Int128)1 << shift;
        Int128 rounded = (Int128)from + (product / divisor);
        Int128 remainder = product % divisor;
        Int128 twiceRemainder = Int128.Abs(remainder) * 2;
        if (twiceRemainder > divisor || (twiceRemainder == divisor && (rounded & 1) != 0))
        {
            rounded += product < 0 ? -1 : 1;
        }

        return checked((long)rounded);
    }
}
