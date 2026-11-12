using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

/// <summary>A closed rectangle with a uniform circular corner radius clamped to half its smaller dimension.</summary>
/// <typeparam name="T">The IEEE floating-point coordinate precision.</typeparam>
public readonly record struct RoundedRect2D<T>
    where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>Initializes a new instance of the <see cref="RoundedRect2D{T}"/> struct.</summary>
    /// <param name="bounds">The outer bounds.</param>
    /// <param name="radius">The requested finite nonnegative corner radius.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is nonfinite or negative.</exception>
    public RoundedRect2D(Rect2D<T> bounds, T radius)
    {
        GeometryScalar<T>.NonNegative(radius, nameof(radius));
        Bounds = bounds;
        Radius = T.Min(radius, T.Min(bounds.Width, bounds.Height) / (T.One + T.One));
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

        // Local coordinates preserve radii smaller than the translation spacing.
        T localX = GeometryScalar<T>.Checked(point.X - Bounds.X);
        T localY = GeometryScalar<T>.Checked(point.Y - Bounds.Y);
        T x = localX - T.Clamp(localX, Radius, Bounds.Width - Radius);
        T y = localY - T.Clamp(localY, Radius, Bounds.Height - Radius);
        return GeometryScalar<T>.Hypot(x, y) <= Radius;
    }
}
