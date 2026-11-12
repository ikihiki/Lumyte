using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed rectangle with a uniform circular corner radius clamped to half its smaller dimension.</summary>
/// <typeparam name="T">The numeric coordinate scalar.</typeparam>
public readonly record struct RoundedRect2D<T>
    where T : INumber<T>
{
    /// <summary>Initializes a new instance of the <see cref="RoundedRect2D{T}"/> struct.</summary>
    /// <param name="bounds">The outer bounds.</param>
    /// <param name="radius">The requested finite nonnegative corner radius.</param>
    /// <remarks>For builtin IEEE scalars, a nonrepresentable half extent is rounded downward for radius clamping; other scalars require an exact half.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The radius is nonfinite or negative.</exception>
    /// <exception cref="ArgumentException">Clamping requires an unrepresentable half extent for a non-IEEE scalar.</exception>
    public RoundedRect2D(Rect2D<T> bounds, T radius)
    {
        GeometryScalar<T>.NonNegative(radius, nameof(radius));
        Bounds = bounds;
        T extent = T.Min(bounds.Width, bounds.Height);

        // Compare twice the requested radius in widened arithmetic before requiring a half extent.
        bool fits = ShapeScalar<T>.IsExact
            ? (2 * ShapeScalar<T>.Exact(radius)) <= ShapeScalar<T>.Exact(extent)
            : radius <= extent - T.Min(radius, extent);

        // Builtin IEEE halves round down if necessary, never enlarging the true half.
        // Discrete, decimal, and custom numbers retain exact-half rejection.
        Radius = fits ? radius : ShapeScalar<T>.HalfDownIeee(extent);
    }

    /// <summary>Gets the outer rectangle.</summary>
    public Rect2D<T> Bounds { get; }

    /// <summary>Gets the effective clamped radius.</summary>
    public T Radius { get; }

    /// <summary>Tests containment including the circular corner boundaries.</summary>
    /// <param name="point">The query point.</param>
    /// <returns>Whether the point belongs to the rounded rectangle.</returns>
    /// <exception cref="OverflowException">A required difference or distance is not finite.</exception>
    public bool Contains(Point2D<T> point)
    {
        if (!Bounds.Contains(point))
        {
            return false;
        }

        if (ShapeScalar<T>.IsExact)
        {
            return ShapeScalar<T>.Rounded(Bounds, Radius, point);
        }

        // Local coordinates preserve radii smaller than the translation spacing.
        T localX = GeometryScalar<T>.Checked(checked(point.X - Bounds.X));
        T localY = GeometryScalar<T>.Checked(checked(point.Y - Bounds.Y));
        T x = T.Max(Radius > localX ? Radius - localX : T.Zero, localX > Bounds.Width - Radius ? localX - (Bounds.Width - Radius) : T.Zero);
        T y = T.Max(Radius > localY ? Radius - localY : T.Zero, localY > Bounds.Height - Radius ? localY - (Bounds.Height - Radius) : T.Zero);
        return ShapeScalar<T>.Ball(x, y, T.Zero, T.Zero, T.Zero, T.Zero, Radius);
    }

    /// <summary>Attempts validated construction.</summary>
    /// <param name="bounds">The outer rectangle.</param>
    /// <param name="radius">The nonnegative radius.</param>
    /// <param name="result">The shape, or default on failure.</param>
    /// <returns>Whether construction succeeded.</returns>
    public static bool TryCreate(Rect2D<T> bounds, T radius, out RoundedRect2D<T> result)
    {
        try
        {
            result = new(bounds, radius);
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
    public RoundedRect2D<TOther> ConvertChecked<TOther>()
        where TOther : INumber<TOther>
        => new(Bounds.ConvertChecked<TOther>(), TOther.CreateChecked(Radius));
}
