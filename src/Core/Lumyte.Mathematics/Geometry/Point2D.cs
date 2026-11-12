using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A finite 2-dimensional coordinate; the default value is the origin.</summary>
/// <typeparam name="T">The ordered numeric coordinate type.</typeparam>
public readonly record struct Point2D<T>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="Point2D{T}"/> struct.</summary>
    /// <param name="x">The finite x coordinate.</param>
    /// <param name="y">The finite y coordinate.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    public Point2D(T x, T y)
    {
        GeometryScalar<T>.Finite(x, nameof(x));
        GeometryScalar<T>.Finite(y, nameof(y));
        X = x;
        Y = y;
    }

    /// <summary>Gets the x coordinate.</summary>
    public T X { get; }

    /// <summary>Gets the y coordinate.</summary>
    public T Y { get; }

    /// <summary>Gets the origin using the numeric type's additive identity.</summary>
    public static Point2D<T> Origin => new(T.Zero, T.Zero);

    /// <summary>Creates a point if every coordinate is finite.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="point">The created point, or the default value on failure.</param>
    /// <returns>Whether every coordinate is finite.</returns>
    public static bool TryCreate(T x, T y, out Point2D<T> point)
    {
        try
        {
            point = new(x, y);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            point = default;
            return false;
        }
    }

    /// <summary>Creates a point from exactly 2 coordinates in component order.</summary>
    /// <param name="coordinates">The coordinates to copy.</param>
    /// <returns>The created point.</returns>
    /// <exception cref="ArgumentException">The coordinate count is not 2.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    public static Point2D<T> FromCoordinates(ReadOnlySpan<T> coordinates)
    {
        if (coordinates.Length != 2)
        {
            throw new ArgumentException("Exactly 2 coordinates are required.", nameof(coordinates));
        }

        return new(coordinates[0], coordinates[1]);
    }

    /// <summary>Copies the components in order, leaving any remaining destination elements unchanged.</summary>
    /// <param name="destination">A destination with room for at least 2 coordinates.</param>
    /// <exception cref="ArgumentException">The destination is too short; no elements are written.</exception>
    public void CopyTo(Span<T> destination)
    {
        if (destination.Length < 2)
        {
            throw new ArgumentException("The destination must hold at least 2 coordinates.", nameof(destination));
        }

        destination[0] = X;
        destination[1] = Y;
    }

    /// <summary>Deconstructs the point into its components.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    public void Deconstruct(out T x, out T y)
    {
        x = X;
        y = Y;
    }

    /// <summary>Returns the Euclidean distance without unnecessary squared overflow.</summary>
    /// <param name="other">The other point.</param>
    /// <returns>The distance in the selected precision.</returns>
    /// <exception cref="OverflowException">A required difference or result is not finite.</exception>
    public T DistanceTo(Point2D<T> other) => DistanceTo<T>(other);

    /// <summary>Returns the distance in an explicit result type. Integral results must be exact.</summary>
    /// <typeparam name="TResult">The result numeric type.</typeparam>
    /// <param name="other">The other point.</param>
    /// <returns>The distance.</returns>
    public TResult DistanceTo<TResult>(Point2D<T> other)
        where TResult : INumber<TResult>
    {
        if (GeometryRational.Supports<T>() || typeof(T) != typeof(TResult))
        {
            return ExactDistanceSquared(other).Root<TResult>();
        }

        return GeometryScalar<TResult>.Hypot(GeometryScalar<TResult>.Checked(checked(TResult.CreateChecked(X) - TResult.CreateChecked(other.X))), GeometryScalar<TResult>.Checked(checked(TResult.CreateChecked(Y) - TResult.CreateChecked(other.Y))));
    }

    /// <summary>Returns the squared distance without a square root.</summary>
    /// <param name="other">The other point.</param>
    /// <returns>The squared distance.</returns>
    public T DistanceSquared(Point2D<T> other) => DistanceSquared<T>(other);

    /// <summary>Returns the squared distance in an explicit result type.</summary>
    /// <typeparam name="TResult">The result numeric type.</typeparam>
    /// <param name="other">The other point.</param>
    /// <returns>The squared distance.</returns>
    public TResult DistanceSquared<TResult>(Point2D<T> other)
        where TResult : INumber<TResult>
    {
        if (GeometryRational.Supports<T>() || typeof(T) != typeof(TResult))
        {
            return ExactDistanceSquared(other).To<TResult>();
        }

        TResult dX = GeometryScalar<TResult>.Checked(checked(TResult.CreateChecked(X) - TResult.CreateChecked(other.X)));
        TResult dY = GeometryScalar<TResult>.Checked(checked(TResult.CreateChecked(Y) - TResult.CreateChecked(other.Y)));
        return GeometryScalar<TResult>.Checked(checked((dX * dX) + (dY * dY)));
    }

    /// <summary>Converts each coordinate using checked numeric conversion; fractional and floating conversions may round.</summary>
    /// <typeparam name="TTo">The target numeric type.</typeparam>
    /// <returns>The converted point.</returns>
    public Point2D<TTo> ConvertChecked<TTo>()
        where TTo : INumber<TTo> => new(TTo.CreateChecked(X), TTo.CreateChecked(Y));

    internal GeometryRational ExactDistanceSquared(Point2D<T> other)
    {
        GeometryRational dX = GeometryRational.From(X) - GeometryRational.From(other.X);
        GeometryRational dY = GeometryRational.From(Y) - GeometryRational.From(other.Y);
        return (dX * dX) + (dY * dY);
    }
}
