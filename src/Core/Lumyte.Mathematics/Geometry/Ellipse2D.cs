using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed axis-aligned ellipse with nonnegative semiaxes, including line and point degeneracies.</summary>
/// <typeparam name="T">The numeric coordinate scalar.</typeparam>
public readonly record struct Ellipse2D<T>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="Ellipse2D{T}"/> struct.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radiusX">The finite nonnegative horizontal semiaxis.</param>
    /// <param name="radiusY">The finite nonnegative vertical semiaxis.</param>
    /// <exception cref="ArgumentOutOfRangeException">A semiaxis is nonfinite or negative.</exception>
    public Ellipse2D(Point2D<T> center, T radiusX, T radiusY)
    {
        GeometryScalar<T>.NonNegative(radiusX, nameof(radiusX));
        GeometryScalar<T>.NonNegative(radiusY, nameof(radiusY));
        Center = center;
        RadiusX = radiusX;
        RadiusY = radiusY;
    }

    /// <summary>Gets the center.</summary>
    public Point2D<T> Center { get; }

    /// <summary>Gets the horizontal semiaxis.</summary>
    public T RadiusX { get; }

    /// <summary>Gets the vertical semiaxis.</summary>
    public T RadiusY { get; }

    /// <summary>Tests containment including the boundary and degenerate semiaxes.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>Whether the point belongs to the ellipse.</returns>
    /// <exception cref="OverflowException">A required coordinate difference is not finite.</exception>
    public bool Contains(Point2D<T> point) => ShapeScalar<T>.Ellipse(point.X, point.Y, Center.X, Center.Y, RadiusX, RadiusY);

    /// <summary>Attempts validated construction.</summary>
    /// <param name="center">The shape center.</param>
    /// <param name="radiusX">The horizontal semiaxis.</param>
    /// <param name="radiusY">The vertical semiaxis.</param>
    /// <param name="result">The shape, or default on failure.</param>
    /// <returns>Whether construction succeeded.</returns>
    public static bool TryCreate(Point2D<T> center, T radiusX, T radiusY, out Ellipse2D<T> result)
    {
        try
        {
            result = new(center, radiusX, radiusY);
            return true;
        }
        catch (ArgumentException)
        {
            result = default;
            return false;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    /// <summary>Converts components with checked numeric conversion.</summary>
    /// <typeparam name="TOther">The destination scalar.</typeparam>
    /// <returns>The validated converted shape.</returns>
    public Ellipse2D<TOther> ConvertChecked<TOther>()
        where TOther : INumber<TOther>
        => new(new(TOther.CreateChecked(Center.X), TOther.CreateChecked(Center.Y)), TOther.CreateChecked(RadiusX), TOther.CreateChecked(RadiusY));
}
