using System.Numerics;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Protects transformed corner evaluation and cached-point bounds reduction.</summary>
public sealed class NumericsBoundsReductionTests
{
    /// <summary>Scaling happens after each transform, including negative scales and reversed corners.</summary>
    [Fact]
    public void ScaledBoundsMatchExplicitCornerOracle()
    {
        var random = new Random(233136);
        for (int i = 0; i < 2000; i++)
        {
            Vector2 minimum = Point();
            Vector2 maximum = Point();
            Matrix3x2 transform = new(Next(), Next(), Next(), Next(), Next(), Next());
            float scale = Next();
            Vector2 a = Vector2.Transform(minimum, transform) * scale;
            Vector2 b = Vector2.Transform(new(maximum.X, minimum.Y), transform) * scale;
            Vector2 c = Vector2.Transform(new(minimum.X, maximum.Y), transform) * scale;
            Vector2 d = Vector2.Transform(maximum, transform) * scale;
            var expectedMinimum = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            var expectedMaximum = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            NumericsGeometry.GetTransformedBounds(minimum, maximum, transform, scale, out Vector2 low, out Vector2 high);
            Assert.Equal(expectedMinimum, low);
            Assert.Equal(expectedMaximum, high);
            NumericsGeometry.GetBounds(a, b, c, d, out low, out high);
            Assert.Equal(expectedMinimum, low);
            Assert.Equal(expectedMaximum, high);
        }

        float Next() => (random.NextSingle() - 0.5f) * 10000;
        Vector2 Point() => new(Next(), Next());
    }
}
