using BenchmarkDotNet.Attributes;
using Lumyte.Mathematics.Geometry;

namespace Lumyte.Mathematics.Benchmark.GeometryQueries;

/// <summary>Closed curved-shape queries with inside, boundary and outside controls.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class ShapeBenchmarks
{
    private readonly Circle2D<double> _circle = new(new(0, 0), 5);
    private readonly Ellipse2D<double> _ellipse = new(new(0, 0), 6, 3);
    private readonly RoundedRect2D<double> _rounded = new(new(0, 0, 10, 8), 2);

    /// <summary>Queries a circle without approximating the boundary.</summary>
    /// <returns>A bit mask of inside, boundary and outside inclusion.</returns>
    [Benchmark]
    public int Circle() => (_circle.Contains(new(1, 1)) ? 1 : 0) + (_circle.Contains(new(3, 4)) ? 2 : 0) + (_circle.Contains(new(4, 4)) ? 4 : 0);

    /// <summary>Queries unequal ellipse radii, including an axis endpoint.</summary>
    /// <returns>A bit mask of inside, boundary and outside inclusion.</returns>
    [Benchmark]
    public int Ellipse() => (_ellipse.Contains(new(1, 1)) ? 1 : 0) + (_ellipse.Contains(new(0, 3)) ? 2 : 0) + (_ellipse.Contains(new(5, 2)) ? 4 : 0);

    /// <summary>Distinguishes a rounded corner cutout from the outer rectangular bounds.</summary>
    /// <returns>A bit mask of interior, boundary and excluded corner inclusion.</returns>
    [Benchmark]
    public int RoundedRectangle() => (_rounded.Contains(new(5, 4)) ? 1 : 0) + (_rounded.Contains(new(0, 2)) ? 2 : 0) + (_rounded.Contains(new(0, 0)) ? 4 : 0);
}
