using BenchmarkDotNet.Attributes;
using Lumyte.Mathematics.Geometry;

namespace Lumyte.Mathematics.Benchmark.GeometryQueries;

/// <summary>Independent spatial queries, distinct from the existing 2D drawing workloads.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class SpatialBenchmarks
{
    private readonly Point3D<double>[] _points = [new(-2, 3, 1), new(4, -5, 7), new(1, 2, -3)];
    private readonly Segment3D<double> _segment = new(new(0, 0, 0), new(6, 0, 0));
    private readonly Sphere3D<double> _sphere = new(new(0, 0, 0), 5);
    private readonly AxisAlignedBox3D<double> _box = new(new(-2, -3, -4), new(4, 5, 6));

    /// <summary>Computes a non-axis-aligned spatial distance.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    public double Distance() => Geometry3D.Distance<double, double>(new(1, 2, 3), new(3, 5, 9));

    /// <summary>Projects an off-axis point into the interior of a spatial segment.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    public Point3D<double> Project() => Geometry3D.ClosestPointOnSegment<double, double>(_segment, new(2, 4, 7));

    /// <summary>Finds independent minima and maxima across spatial coordinates.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    public AxisAlignedBox3D<double> Bounds() => Geometry3D.GetBounds<double>(_points);

    /// <summary>Includes inside, exact-boundary and outside sphere queries.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    public int SphereQueries() => (_sphere.Contains(new(0, 0, 0)) ? 1 : 0) + (_sphere.Contains(new(3, 4, 0)) ? 2 : 0) + (_sphere.Contains(new(3, 4, 1)) ? 4 : 0);

    /// <summary>Intersects overlapping spatial boxes and returns the actual intersection.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    public AxisAlignedBox3D<double> Intersect()
    {
        if (!_box.TryIntersect(new(new(1, 2, 3), new(8, 9, 10)), out AxisAlignedBox3D<double> result))
        {
            throw new InvalidOperationException("Expected overlapping boxes.");
        }

        return result;
    }
}
