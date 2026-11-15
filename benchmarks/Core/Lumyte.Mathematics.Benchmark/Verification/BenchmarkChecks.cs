using Lumyte.Mathematics.Benchmark.ColorOperations;
using Lumyte.Mathematics.Benchmark.GeometryQueries;
using Lumyte.Mathematics.Benchmark.InterpolationFunctions;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Mathematics.Benchmark;

/// <summary>Independent numerical workload oracles.</summary>
public static class BenchmarkChecks
{
    /// <summary>Checks original bounds and distances plus nonlinear color and timing endpoints.</summary>
    public static void Verify()
    {
        var bounds = new BoundsBenchmarks();
        bounds.Setup();
        bounds.Verify();
        foreach (int length in new[] { 0, 3, 31, 32, 33, 1024, 65536 })
        {
            var batch = new BatchBenchmark { Length = length };
            batch.Setup();
            batch.Verify();
        }

        var math = new MathBenchmarks { Amount = 0 };
        var geometry = new GeometryBenchmarks();
        if (math.Linear() != -15 || geometry.Distance() != 5 || !geometry.ContainsClosed() || geometry.ContainsHalfOpen() || geometry.Flatten() < 2)
        {
            throw new InvalidOperationException("Geometry oracle mismatch.");
        }

        foreach (float amount in new[] { 0f, 0.375f, 1f, 1.25f })
        {
            var interpolation = new MathBenchmarks { Amount = amount };
            float linear = interpolation.Linear();
            if (!float.IsFinite(linear) || interpolation.Integer() != IntegerInterpolation.Linear(-1500, 4200, amount)
                || !float.IsFinite(new TimingBenchmarks { Amount = Math.Min(amount, 1) }.Hermite())
                || Math.Abs(interpolation.Slerp().Length() - 1) > 0.00001)
            {
                throw new InvalidOperationException("Interpolation profile mismatch.");
            }

            interpolation.DelegateBezier();
            interpolation.EaseIn();
            interpolation.EaseOut();
            interpolation.EaseInOut();
            geometry.CurveEvaluate();
        }

        if (geometry.CoordinateConvert().X != 123 || geometry.Projection() != new Lumyte.Mathematics.Geometry.Point2D<double>(1.92, 2.56)
            || !geometry.Intersection() || !geometry.PolygonNonZero() || !geometry.PolygonEvenOdd())
        {
            throw new InvalidOperationException("Projection or polygon oracle mismatch.");
        }

        geometry.BoundsUnion();
        geometry.Affine();
        geometry.TimingConstruct();

        for (int i = 1; i < 100; i++)
        {
            double x = i / 100.0;
            double low = 0;
            double high = 1;
            for (int step = 0; step < 60; step++)
            {
                double middle = (low + high) / 2;
                double inverse = 1 - middle;
                double actualX = (3 * inverse * inverse * middle * 0.25) + (3 * inverse * middle * middle * 0.25) + (middle * middle * middle);
                if (actualX < x)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            double t = (low + high) / 2;
            double expected = (3 * (1 - t) * (1 - t) * t * 0.1) + (3 * (1 - t) * t * t) + (t * t * t);
            if (Math.Abs(new CubicBezierTiming(0.25, 0.1, 0.25, 1).Transform(x) - expected) > 0.00001)
            {
                throw new InvalidOperationException("Timing bisection oracle mismatch.");
            }
        }

        var color = new ColorBenchmarks();
        if (color.Normalize().B != 1 || color.Quantize().A != 128 || Math.Abs(color.PixelDecode().G - (128f / 255f)) > 0.00001f || RawRgb565.FromSrgb(new SRgba<float>(1, 0, 0, 1)).PackedValue != 0xf800)
        {
            throw new InvalidOperationException("Color code oracle mismatch.");
        }

        color.Decode();
        color.Encode();
        color.Grayscale();
        color.Premultiply();
        color.Unpremultiply();
        color.SourceOver();
        color.Interpolate();
        color.Matrix();
        color.PixelWrite();
        color.Pack();
        color.Unpack();
        foreach (float hue in new[] { 0f, 60f, 120f, 180f, 240f, 300f, 420f })
        {
            var model = new ColorModelBenchmarks { Hue = hue };
            model.Hsl();
            model.Hsv();
        }

        var red = SRgba<float>.FromHslSrgbDegrees(0f, 1f, 0.5f, 1f);
        if (red.R != 1 || red.G != 0 || red.B != 0 || !CssColors.TryGet("ReD", out SRgba8 named) || named.R != 255)
        {
            throw new InvalidOperationException("Named/model color mismatch.");
        }
    }
}
