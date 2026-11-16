using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

internal static class GeometryScalar<T>
    where T : INumber<T>
{
    internal static bool IsIeee => typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(Half);

    internal static void Finite(T value, string name)
    {
        if (!T.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, "A finite value is required.");
        }
    }

    internal static void NonNegative(T value, string name)
    {
        Finite(value, name);
        if (value < T.Zero)
        {
            throw new ArgumentOutOfRangeException(name, "A nonnegative value is required.");
        }
    }

    internal static T Checked(T value) => T.IsFinite(value) ? value : throw new OverflowException("The geometry result is not finite in the selected precision.");

    internal static T Sqrt(T value)
    {
        if (typeof(T) == typeof(double))
        {
            return Checked((T)(object)double.Sqrt((double)(object)value));
        }

        if (typeof(T) == typeof(float))
        {
            return Checked((T)(object)float.Sqrt((float)(object)value));
        }

        if (typeof(T) == typeof(Half))
        {
            return Checked((T)(object)Half.Sqrt((Half)(object)value));
        }

        if (typeof(T) == typeof(decimal))
        {
            decimal v = (decimal)(object)value;
            if (v == 0)
            {
                return T.Zero;
            }

            decimal root = v >= 1 ? v : 1;
            decimal previous = 0;
            for (int i = 0; i < 256; i++)
            {
                decimal next = (root / 2) + (v / root / 2);
                if (next == root || next == previous)
                {
                    return (T)(object)next;
                }

                previous = root;
                root = next;
            }

            return (T)(object)root;
        }

        if (GeometryRational.Supports<T>())
        {
            return GeometryRational.From(value).Root<T>();
        }

        throw new NotSupportedException("No square-root policy exists for this result type; select an explicit supported result type.");
    }

    internal static T Hypot(T x, T y)
    {
        if (typeof(T) == typeof(double))
        {
            return (T)(object)IeeeGeometryScalar<double>.Hypot((double)(object)x, (double)(object)y);
        }

        if (typeof(T) == typeof(float))
        {
            return (T)(object)IeeeGeometryScalar<float>.Hypot((float)(object)x, (float)(object)y);
        }

        if (typeof(T) == typeof(Half))
        {
            return (T)(object)IeeeGeometryScalar<Half>.Hypot((Half)(object)x, (Half)(object)y);
        }

        if (GeometryRational.Supports<T>())
        {
            return ((GeometryRational.From(x) * GeometryRational.From(x)) + (GeometryRational.From(y) * GeometryRational.From(y))).Root<T>();
        }

        return Sqrt(Checked(checked((x * x) + (y * y))));
    }

    internal static T Hypot(T x, T y, T z)
    {
        if (typeof(T) == typeof(double))
        {
            return (T)(object)IeeeGeometryScalar<double>.Hypot((double)(object)x, (double)(object)y, (double)(object)z);
        }

        if (typeof(T) == typeof(float))
        {
            return (T)(object)IeeeGeometryScalar<float>.Hypot((float)(object)x, (float)(object)y, (float)(object)z);
        }

        if (typeof(T) == typeof(Half))
        {
            return (T)(object)IeeeGeometryScalar<Half>.Hypot((Half)(object)x, (Half)(object)y, (Half)(object)z);
        }

        if (GeometryRational.Supports<T>())
        {
            return ((GeometryRational.From(x) * GeometryRational.From(x)) + (GeometryRational.From(y) * GeometryRational.From(y)) + (GeometryRational.From(z) * GeometryRational.From(z))).Root<T>();
        }

        return Sqrt(Checked(checked((x * x) + (y * y) + (z * z))));
    }

    internal static (T Numerator, T Denominator, int Exponent) Projection(T dx, T dy, T dz, T px, T py, T pz)
    {
        if (typeof(T) == typeof(double))
        {
            (double Numerator, double Denominator, int Exponent) result = IeeeGeometryScalar<double>.Projection((double)(object)dx, (double)(object)dy, (double)(object)dz, (double)(object)px, (double)(object)py, (double)(object)pz);
            return ((T)(object)result.Numerator, (T)(object)result.Denominator, result.Exponent);
        }

        if (typeof(T) == typeof(float))
        {
            (float Numerator, float Denominator, int Exponent) result = IeeeGeometryScalar<float>.Projection((float)(object)dx, (float)(object)dy, (float)(object)dz, (float)(object)px, (float)(object)py, (float)(object)pz);
            return ((T)(object)result.Numerator, (T)(object)result.Denominator, result.Exponent);
        }

        if (typeof(T) == typeof(Half))
        {
            (Half Numerator, Half Denominator, int Exponent) result = IeeeGeometryScalar<Half>.Projection((Half)(object)dx, (Half)(object)dy, (Half)(object)dz, (Half)(object)px, (Half)(object)py, (Half)(object)pz);
            return ((T)(object)result.Numerator, (T)(object)result.Denominator, result.Exponent);
        }

        throw new NotSupportedException("Use exact projection for non-IEEE coordinates.");
    }

    internal static T Lerp(T start, T end, (T Numerator, T Denominator, int Exponent) amount)
    {
        if (typeof(T) == typeof(double))
        {
            return (T)(object)IeeeGeometryScalar<double>.Lerp((double)(object)start, (double)(object)end, ((double)(object)amount.Numerator, (double)(object)amount.Denominator, amount.Exponent));
        }

        if (typeof(T) == typeof(float))
        {
            return (T)(object)IeeeGeometryScalar<float>.Lerp((float)(object)start, (float)(object)end, ((float)(object)amount.Numerator, (float)(object)amount.Denominator, amount.Exponent));
        }

        if (typeof(T) == typeof(Half))
        {
            return (T)(object)IeeeGeometryScalar<Half>.Lerp((Half)(object)start, (Half)(object)end, ((Half)(object)amount.Numerator, (Half)(object)amount.Denominator, amount.Exponent));
        }

        throw new NotSupportedException("Use exact projection for non-IEEE coordinates.");
    }

    internal static T Orientation(Point2D<T> a, Point2D<T> b, Point2D<T> c)
    {
        if (typeof(T) == typeof(double))
        {
            return (T)(object)IeeeGeometryScalar<double>.Orientation(new Point2D<double>(double.CreateChecked(a.X), double.CreateChecked(a.Y)), new Point2D<double>(double.CreateChecked(b.X), double.CreateChecked(b.Y)), new Point2D<double>(double.CreateChecked(c.X), double.CreateChecked(c.Y)));
        }

        if (typeof(T) == typeof(float))
        {
            return (T)(object)IeeeGeometryScalar<float>.Orientation(new Point2D<float>(float.CreateChecked(a.X), float.CreateChecked(a.Y)), new Point2D<float>(float.CreateChecked(b.X), float.CreateChecked(b.Y)), new Point2D<float>(float.CreateChecked(c.X), float.CreateChecked(c.Y)));
        }

        if (typeof(T) == typeof(Half))
        {
            return (T)(object)IeeeGeometryScalar<Half>.Orientation(new Point2D<Half>(Half.CreateChecked(a.X), Half.CreateChecked(a.Y)), new Point2D<Half>(Half.CreateChecked(b.X), Half.CreateChecked(b.Y)), new Point2D<Half>(Half.CreateChecked(c.X), Half.CreateChecked(c.Y)));
        }

        return T.CreateChecked(OrientationSign(a, b, c));
    }

    internal static int OrientationSign(Point2D<T> a, Point2D<T> b, Point2D<T> c)
    {
        if (IsIeee)
        {
            return T.Sign(Orientation(a, b, c));
        }

        if (GeometryRational.Supports<T>())
        {
            GeometryRational x = GeometryRational.From(b.X) - GeometryRational.From(a.X);
            GeometryRational y = GeometryRational.From(b.Y) - GeometryRational.From(a.Y);
            GeometryRational u = GeometryRational.From(c.X) - GeometryRational.From(a.X);
            GeometryRational v = GeometryRational.From(c.Y) - GeometryRational.From(a.Y);
            return ((x * v) - (y * u)).Numerator.Sign;
        }

        return T.Sign(Checked(checked(((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X)))));
    }
}
