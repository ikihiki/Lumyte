using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Checks independent IEEE arithmetic, span tails and alias contracts.</summary>
public sealed class GeometryBatchTests
{
    /// <summary>All short lengths and vector tails match separate scalar arithmetic, including special values.</summary>
    [Fact]
    public void SingleMatchesScalar()
    {
        float[] special = [0, -0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.Epsilon, -float.Epsilon, 1.25f, -123.5f];
        var random = new Random(17);
        for (int length = 0; length < 140; length++)
        {
            float[] x = new float[length];
            float[] y = new float[length];
            float[] output = new float[length + 1];
            output[length] = -17;
            for (int i = 0; i < length; i++)
            {
                x[i] = i % 3 == 0 ? special[i % special.Length] : (float)((random.NextDouble() * 100) - 50);
                y[i] = i % 2 == 0 ? special[(i + 3) % special.Length] : (float)((random.NextDouble() * 100) - 50);
            }

            foreach (float point in special)
            {
                Geometry2D.SquaredDistancesBcl(x, y, point, 1.75f, output);
                for (int i = 0; i < length; i++)
                {
                    float dx = x[i] - point;
                    float dy = y[i] - 1.75f;
                    float expected = (dx * dx) + (dy * dy);
                    Assert.True(float.IsNaN(expected) ? float.IsNaN(output[i]) : BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(output[i]));
                }
            }

            Assert.Equal(-17f, output[length]);
        }
    }

    /// <summary>Exact alias is supported; shifted aliases and invalid lengths fail before any writes.</summary>
    [Fact]
    public void SingleValidatesAliasesAndLengths()
    {
        float[] x = Enumerable.Range(0, 33).Select(i => (float)i).ToArray();
        float[] y = Enumerable.Range(0, 33).Select(i => (float)(i * 2)).ToArray();
        float[] copy = (float[])x.Clone();
        Geometry2D.SquaredDistancesBcl(x, y, 0, 0, x);
        for (int i = 0; i < x.Length; i++)
        {
            Assert.Equal((copy[i] * copy[i]) + (y[i] * y[i]), x[i]);
        }

        Array.Copy(copy, x, x.Length);
        Geometry2D.SquaredDistancesBcl(x, y, 0, 0, y);
        for (int i = 0; i < x.Length; i++)
        {
            Assert.Equal((float)(i * i * 5), y[i]);
        }

        Array.Copy(copy, x, x.Length);
        Assert.Throws<ArgumentException>(() => Geometry2D.SquaredDistancesBcl(x.AsSpan(0, 32), y.AsSpan(0, 32), 0, 0, x.AsSpan(1)));
        Assert.Throws<ArgumentException>(() => Geometry2D.SquaredDistancesBcl(x.AsSpan(1), y.AsSpan(0, 32), 0, 0, x.AsSpan(0, 32)));
        Assert.Throws<ArgumentException>(() => Geometry2D.SquaredDistancesBcl(y.AsSpan(0, 32), x.AsSpan(0, 32), 0, 0, x.AsSpan(1)));
        Assert.Throws<ArgumentException>(() => Geometry2D.SquaredDistancesBcl(x, y.AsSpan(1), 0, 0, x));
        Assert.Throws<ArgumentException>(() => Geometry2D.SquaredDistancesBcl(x, y, 0, 0, x.AsSpan(1)));
        Assert.Equal(copy, x);
        Geometry2D.SquaredDistancesBcl(x, x, 0, 0, x);
        for (int i = 0; i < x.Length; i++)
        {
            Assert.Equal((float)(i * i * 2), x[i]);
        }
    }

    /// <summary>All short lengths and vector tails match separate scalar arithmetic, including special values.</summary>
    [Fact]
    public void DoubleMatchesScalar()
    {
        double[] special = [0, -0d, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.Epsilon, -double.Epsilon, 1.25f, -123.5f];
        var random = new Random(17);
        for (int length = 0; length < 140; length++)
        {
            double[] x = new double[length];
            double[] y = new double[length];
            double[] output = new double[length + 1];
            output[length] = -17;
            for (int i = 0; i < length; i++)
            {
                x[i] = i % 3 == 0 ? special[i % special.Length] : (double)((random.NextDouble() * 100) - 50);
                y[i] = i % 2 == 0 ? special[(i + 3) % special.Length] : (double)((random.NextDouble() * 100) - 50);
            }

            foreach (double point in special)
            {
                Geometry2D.SquaredDistancesBcl(x, y, point, 1.75d, output);
                for (int i = 0; i < length; i++)
                {
                    double dx = x[i] - point;
                    double dy = y[i] - 1.75d;
                    double expected = (dx * dx) + (dy * dy);
                    Assert.True(double.IsNaN(expected) ? double.IsNaN(output[i]) : BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(output[i]));
                }
            }

            Assert.Equal(-17d, output[length]);
        }
    }

    /// <summary>Exact alias is supported; shifted aliases and invalid lengths fail before any writes.</summary>
    [Fact]
    public void DoubleValidatesAliasesAndLengths()
    {
        double[] x = Enumerable.Range(0, 33).Select(i => (double)i).ToArray();
        double[] y = Enumerable.Range(0, 33).Select(i => (double)(i * 2)).ToArray();
        double[] copy = (double[])x.Clone();
        Geometry2D.SquaredDistancesBcl(x, y, 0, 0, x);
        for (int i = 0; i < x.Length; i++)
        {
            Assert.Equal((copy[i] * copy[i]) + (y[i] * y[i]), x[i]);
        }

        Array.Copy(copy, x, x.Length);
        Geometry2D.SquaredDistancesBcl(x, y, 0, 0, y);
        for (int i = 0; i < x.Length; i++)
        {
            Assert.Equal((double)(i * i * 5), y[i]);
        }

        Array.Copy(copy, x, x.Length);
        Assert.Throws<ArgumentException>(() => Geometry2D.SquaredDistancesBcl(x.AsSpan(0, 32), y.AsSpan(0, 32), 0, 0, x.AsSpan(1)));
        Assert.Throws<ArgumentException>(() => Geometry2D.SquaredDistancesBcl(x.AsSpan(1), y.AsSpan(0, 32), 0, 0, x.AsSpan(0, 32)));
        Assert.Throws<ArgumentException>(() => Geometry2D.SquaredDistancesBcl(y.AsSpan(0, 32), x.AsSpan(0, 32), 0, 0, x.AsSpan(1)));
        Assert.Throws<ArgumentException>(() => Geometry2D.SquaredDistancesBcl(x, y.AsSpan(1), 0, 0, x));
        Assert.Throws<ArgumentException>(() => Geometry2D.SquaredDistancesBcl(x, y, 0, 0, x.AsSpan(1)));
        Assert.Equal(copy, x);
        Geometry2D.SquaredDistancesBcl(x, x, 0, 0, x);
        for (int i = 0; i < x.Length; i++)
        {
            Assert.Equal((double)(i * i * 2), x[i]);
        }
    }
}
