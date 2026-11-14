using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Mathematics.Geometry;

namespace Lumyte.Mathematics.Benchmark;

/// <summary>Original seed-9417 transformed-corner workload without a stopwatch runner.</summary>
public class BoundsBenchmarks
{
    private readonly Vector2[] _first = new Vector2[256];
    private readonly Vector2[] _last = new Vector2[256];
    private readonly Matrix3x2[] _transforms = new Matrix3x2[256];

    /// <summary>Prepares the original 256 coordinate and matrix tuples.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(9417);
        for (int i = 0; i < 256; i++)
        {
            _first[i] = new(Next(), Next());
            _last[i] = new(Next(), Next());
            _transforms[i] = new(Next(), Next(), Next(), Next(), Next(), Next());
        }

        float Next() => (random.NextSingle() - 0.5f) * 100;
    }

    /// <summary>Reduces four transformed corners through the production API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-MATH-07")]
    public Vector4 TransformedBounds()
    {
        Vector4 result = default;
        for (int i = 0; i < 256; i++)
        {
            Geometry2D.GetTransformedBoundsBcl(_first[i], _last[i], _transforms[i], out Vector2 minimum, out Vector2 maximum);
            result += new Vector4(minimum, maximum.X, maximum.Y);
        }

        return result;
    }

    internal void Verify()
    {
        for (int i = 0; i < 256; i++)
        {
            Vector2 first = _first[i];
            Vector2 last = _last[i];
            Matrix3x2 transform = _transforms[i];
            Vector2 minimum = new(float.PositiveInfinity);
            Vector2 maximum = new(float.NegativeInfinity);
            Vector2[] corners = [first, new(first.X, last.Y), new(last.X, first.Y), last];
            foreach (Vector2 corner in corners)
            {
                var point = Vector2.Transform(corner, transform);
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }

            Geometry2D.GetTransformedBoundsBcl(first, last, transform, out Vector2 actualMinimum, out Vector2 actualMaximum);
            if (minimum != actualMinimum || maximum != actualMaximum)
            {
                throw new InvalidOperationException($"Bounds oracle mismatch at {i}.");
            }
        }
    }
}
