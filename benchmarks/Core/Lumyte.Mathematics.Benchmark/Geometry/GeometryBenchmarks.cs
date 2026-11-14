using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Mathematics.Geometry;

namespace Lumyte.Mathematics.Benchmark;

/// <summary>Geometry paths without unrelated interpolation parameters.</summary>
[BenchmarkCategory("Core", "Mathematics", "Geometry", "Screening", "Comparison", "Diagnostic")]
public class GeometryBenchmarks
{
    private readonly Point2D<double> _start = new(0, 0);
    private readonly Point2D<double> _end = new(3, 4);
    private readonly Rect2D<double> _rectangle = new(0, 0, 10, 20);
    private readonly Matrix3x2 _transform = new(2, 1, -1, 3, 4, -5);
    private readonly Point2D<double>[] _polygon = [new(0, 0), new(8, 0), new(4, 3), new(8, 8), new(0, 8)];
    private readonly List<Point2D<double>> _flattened = new(1024);

    /// <summary>Executes TimingConstruct through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public CubicBezierTiming TimingConstruct() => new(0.25, 0.1, 0.25, 1);

    /// <summary>Executes CoordinateConvert through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public Point2D<double> CoordinateConvert() => new Point2D<int>(123, -456).ConvertChecked<double>();

    /// <summary>Executes Distance through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public double Distance() => _start.DistanceTo(_end);

    /// <summary>Executes Projection through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public Point2D<double> Projection() => new Segment2D<double>(_start, _end).ClosestPoint(new(8, -2));

    /// <summary>Executes Intersection through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public bool Intersection() => new Segment2D<double>(_start, _end).Intersects(new(new(0, 5), new(10, -2)));

    /// <summary>Executes ContainsClosed through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public bool ContainsClosed() => _rectangle.Contains(new(10, 20));

    /// <summary>Executes ContainsHalfOpen through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public bool ContainsHalfOpen() => _rectangle.ContainsHalfOpen(new(10, 20));

    /// <summary>Executes BoundsUnion through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public Rect2D<double> BoundsUnion() => _rectangle.Union(new(-3, -2, 8, 9));

    /// <summary>Executes Affine through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public Point2D<double> Affine() => Geometry2D.TransformAffineFused(_start, _transform);

    /// <summary>Executes PolygonNonZero through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public bool PolygonNonZero() => Geometry2D.ContainsPolygonClosed<double>(_polygon, new(2, 2));

    /// <summary>Executes PolygonEvenOdd through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public bool PolygonEvenOdd() => Geometry2D.ContainsPolygonEvenOddClosed<double>(_polygon, new(2, 2));

    /// <summary>Executes CurveEvaluate through the public product API.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public Point2D<double> CurveEvaluate() => Geometry2D.EvaluateCubicBezier(_start, new Point2D<double>(2, 8), new Point2D<double>(6, -3), _end, 0.375);

    /// <summary>Flattens a curve with bounded output and a reused cleared destination.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    public int Flatten()
    {
        _flattened.Clear();
        if (!Geometry2D.TryFlattenCubicBezier(_start, new Point2D<double>(2, 8), new Point2D<double>(6, -3), _end, 0.25, _flattened, 10, 1024))
        {
            throw new InvalidOperationException("Curve budget exceeded.");
        }

        return _flattened.Count;
    }
}
