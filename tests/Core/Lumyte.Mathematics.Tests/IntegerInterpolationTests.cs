using System.Numerics;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Verifies exact integer rounding independently of animation and floating-point decimal conversion.</summary>
public sealed class IntegerInterpolationTests
{
    /// <summary>Checks rounding at binary weights whose decimal conversion changes a midpoint.</summary>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The ending value.</param>
    /// <param name="amount">The binary floating-point weight.</param>
    /// <param name="expected">The correctly rounded integer.</param>
    [Theory]
    [InlineData(0, 4194304, 0.9f, 3774874)]
    [InlineData(1, 67108865, 0.1f, 6710888)]
    [InlineData(0, -4194304, 0.9f, -3774874)]
    [InlineData(-1, -67108865, 0.1f, -6710888)]
    [InlineData(1, 2, 0.5f, 2)]
    [InlineData(2, 3, 0.5f, 2)]
    [InlineData(-1, 0, 0.5f, 0)]
    [InlineData(-3, 0, 0.5f, -2)]
    [InlineData(1, 2, -1f, 0)]
    public void LinearRoundsExactWeights(long from, long to, float amount, long expected) => Assert.Equal(expected, IntegerInterpolation.Linear(from, to, amount));

    /// <summary>Checks exact endpoints, cancellation and extrapolation across the full integer range.</summary>
    [Fact]
    public void LinearPreservesFullRangeEndpoints()
    {
        Assert.Equal(long.MinValue, IntegerInterpolation.Linear(long.MinValue, long.MaxValue, 0));
        Assert.Equal(long.MaxValue, IntegerInterpolation.Linear(long.MinValue, long.MaxValue, 1));
        Assert.Equal(0, IntegerInterpolation.Linear(long.MinValue, long.MaxValue, 0.5f));
        Assert.Equal(long.MaxValue, IntegerInterpolation.Linear(long.MaxValue, long.MaxValue, float.MaxValue));
        Assert.Equal(long.MinValue, IntegerInterpolation.Linear(long.MinValue, long.MaxValue, float.Epsilon));
        Assert.Equal(long.MaxValue, IntegerInterpolation.Linear(long.MaxValue, long.MinValue, -float.Epsilon));
        Assert.Equal(0, IntegerInterpolation.Linear(long.MinValue, long.MinValue + 1, MathF.ScaleB(1, 63)));
        Assert.Equal(-1, IntegerInterpolation.Linear(long.MaxValue, long.MaxValue - 1, MathF.ScaleB(1, 63)));
        Assert.Throws<OverflowException>(() => IntegerInterpolation.Linear(0, 1, MathF.ScaleB(1, 63)));
        Assert.Throws<OverflowException>(() => IntegerInterpolation.Linear(long.MinValue, long.MinValue + 1, MathF.ScaleB(1, 64)));
        Assert.Throws<OverflowException>(() => IntegerInterpolation.Linear(0, 1, float.MaxValue));
        Assert.Throws<OverflowException>(() => IntegerInterpolation.Linear(0, 1, -float.MaxValue));
    }

    /// <summary>Checks invalid weights even when the endpoints are equal.</summary>
    /// <param name="amount">The invalid weight.</param>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void LinearRejectsNonFiniteWeights(float amount) => Assert.Throws<ArgumentOutOfRangeException>(() => IntegerInterpolation.Linear(1, 1, amount));

    /// <summary>Compares normal, subnormal and extrapolating weights to an independent exact rational oracle.</summary>
    [Fact]
    public void LinearMatchesRationalArithmetic()
    {
        long[] endpoints = [long.MinValue, long.MinValue + 1, -67108865, -1, 0, 1, 4194304, long.MaxValue - 1, long.MaxValue];
        float[] weights = [-float.MaxValue, -MathF.ScaleB(1, 64), -MathF.ScaleB(1, 63), -1.5f, -1, -0.5f, -float.Epsilon, 0, float.Epsilon, BitConverter.UInt32BitsToSingle(0x7fffff), MathF.ScaleB(1, -126), MathF.ScaleB(1, -65), MathF.ScaleB(1, -64), 0.1f, 0.5f, 0.9f, MathF.BitDecrement(1), 1, MathF.BitIncrement(1), 1.5f, MathF.ScaleB(1, 63), MathF.ScaleB(1, 64), float.MaxValue];
        foreach (long from in endpoints)
        {
            foreach (long to in endpoints)
            {
                foreach (float amount in weights)
                {
                    AssertMatchesOracle(from, to, amount);
                }
            }
        }

        var random = new Random(0x39a421);
        Span<byte> bytes = stackalloc byte[20];
        for (int i = 0; i < 4000; i++)
        {
            random.NextBytes(bytes);
            long from = BitConverter.ToInt64(bytes);
            long to = BitConverter.ToInt64(bytes[8..]);
            float amount = BitConverter.ToSingle(bytes[16..]);
            if (float.IsFinite(amount))
            {
                AssertMatchesOracle(from, to, amount);
            }

            AssertMatchesOracle(from, to, random.NextSingle());
        }
    }

    /// <summary>Checks that repeated exact rounding does not allocate.</summary>
    [Fact]
    public void LinearDoesNotAllocate()
    {
        long checksum = 0;
        for (int i = 0; i < 1000; i++)
        {
            checksum += IntegerInterpolation.Linear(long.MinValue, long.MaxValue, 0.5f);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            checksum += IntegerInterpolation.Linear(long.MinValue, long.MaxValue, 0.5f);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, checksum);
        Assert.Equal(0, allocated);
    }

    private static void AssertMatchesOracle(long from, long to, float amount)
    {
        BigInteger divisor = BigInteger.One << 149;
        var weight = new BigInteger(Math.ScaleB(amount, 149));
        BigInteger numerator = (from * divisor) + (((BigInteger)to - from) * weight);
        var rounded = BigInteger.DivRem(numerator, divisor, out BigInteger remainder);
        BigInteger twiceRemainder = BigInteger.Abs(remainder) * 2;
        if (twiceRemainder > divisor || (twiceRemainder == divisor && !rounded.IsEven))
        {
            rounded += numerator.Sign;
        }

        if (rounded < long.MinValue || rounded > long.MaxValue)
        {
            Assert.Throws<OverflowException>(() => IntegerInterpolation.Linear(from, to, amount));
        }
        else
        {
            Assert.Equal((long)rounded, IntegerInterpolation.Linear(from, to, amount));
        }
    }
}
