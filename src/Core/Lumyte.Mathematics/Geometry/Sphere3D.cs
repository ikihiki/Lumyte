using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed ball with a finite center and nonnegative radius.</summary>
/// <typeparam name="T">The numeric coordinate scalar.</typeparam>
public readonly record struct Sphere3D<T>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="Sphere3D{T}"/> struct.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radius">The finite nonnegative radius.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is nonfinite or negative.</exception>
    public Sphere3D(Point3D<T> center, T radius)
    {
        GeometryScalar<T>.NonNegative(radius, nameof(radius));
        Center = center;
        Radius = radius;
    }

    /// <summary>Gets the center.</summary>
    public Point3D<T> Center { get; }

    /// <summary>Gets the radius.</summary>
    public T Radius { get; }

    /// <summary>Tests containment including the boundary; zero radius contains only the center.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>Whether the distance to the center does not exceed the radius.</returns>
    /// <exception cref="OverflowException">A required difference or distance is not finite.</exception>
    public bool Contains(Point3D<T> point) => ShapeScalar<T>.Ball(point.X, point.Y, point.Z, Center.X, Center.Y, Center.Z, Radius);

    /// <summary>Attempts validated construction.</summary>
    /// <param name="center">The shape center.</param>
    /// <param name="radius">The nonnegative radius.</param>
    /// <param name="result">The shape, or default on failure.</param>
    /// <returns>Whether construction succeeded.</returns>
    public static bool TryCreate(Point3D<T> center, T radius, out Sphere3D<T> result)
    {
        try
        {
            result = new(center, radius);
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
    public Sphere3D<TOther> ConvertChecked<TOther>()
        where TOther : INumber<TOther>
        => new(new(TOther.CreateChecked(Center.X), TOther.CreateChecked(Center.Y), TOther.CreateChecked(Center.Z)), TOther.CreateChecked(Radius));
}
