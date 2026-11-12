using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

// Exact rational arithmetic for discrete/decimal predicates; custom numbers use
// their own checked arithmetic, without an implicit conversion to double.
internal static class ShapeScalar<T>
    where T : INumber<T>
{
    internal static bool IsExact => typeof(T) == typeof(decimal) || typeof(T) == typeof(BigInteger)
        || typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) || typeof(T) == typeof(short)
        || typeof(T) == typeof(ushort) || typeof(T) == typeof(int) || typeof(T) == typeof(uint)
        || typeof(T) == typeof(long) || typeof(T) == typeof(ulong) || typeof(T) == typeof(nint)
        || typeof(T) == typeof(nuint) || typeof(T) == typeof(Int128) || typeof(T) == typeof(UInt128);

    internal static BigInteger Exact(T value)
    {
        if (typeof(T) != typeof(decimal))
        {
            return BigInteger.CreateChecked(value);
        }

        int[] bits = decimal.GetBits((decimal)(object)value);
        BigInteger coefficient = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
        coefficient *= BigInteger.Pow(10, 28 - ((bits[3] >> 16) & 255));
        return bits[3] < 0 ? -coefficient : coefficient;
    }

    internal static bool Ball(T x, T y, T z, T cx, T cy, T cz, T radius)
    {
        if (IsExact)
        {
            BigInteger dx = Exact(x) - Exact(cx);
            BigInteger dy = Exact(y) - Exact(cy);
            BigInteger dz = Exact(z) - Exact(cz);
            BigInteger r = Exact(radius);
            return (dx * dx) + (dy * dy) + (dz * dz) <= r * r;
        }

        T a = GeometryScalar<T>.Checked(checked(x - cx));
        T b = GeometryScalar<T>.Checked(checked(y - cy));
        T c = GeometryScalar<T>.Checked(checked(z - cz));

        // Preserve the IEEE finite-distance overflow contract and scale away
        // squared overflow/underflow. No integer division reaches this branch.
        if (typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(Half))
        {
            return GeometryScalar<T>.Hypot(a, b, c) <= radius;
        }

        return checked(checked(a * a) + checked(b * b) + checked(c * c)) <= checked(radius * radius);
    }

    internal static bool Ellipse(T x, T y, T cx, T cy, T rx, T ry)
    {
        if (IsExact)
        {
            var dx = BigInteger.Abs(Exact(x) - Exact(cx));
            var dy = BigInteger.Abs(Exact(y) - Exact(cy));
            BigInteger a = Exact(rx);
            BigInteger b = Exact(ry);
            return dx <= a && dy <= b && (a.IsZero || b.IsZero || ((dx * dx * b * b) + (dy * dy * a * a) <= a * a * b * b));
        }

        T u = T.Abs(GeometryScalar<T>.Checked(checked(x - cx)));
        T v = T.Abs(GeometryScalar<T>.Checked(checked(y - cy)));
        if (u > rx || v > ry)
        {
            return false;
        }

        if (rx == T.Zero || ry == T.Zero)
        {
            return true;
        }

        if (typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(Half))
        {
            return GeometryScalar<T>.Hypot(u / rx, v / ry) <= T.One;
        }

        return checked(checked(u * u * ry * ry) + checked(v * v * rx * rx)) <= checked(rx * rx * ry * ry);
    }

    // Rounded corners alone permit downward approximation of builtin IEEE halves.
    // Center factories continue to require HalfExact, including for IEEE scalars.
    internal static T HalfDownIeee(T value)
    {
        if (typeof(T) == typeof(double))
        {
            double extent = (double)(object)value;
            double half = extent / 2;
            return (T)(object)(half > extent - half ? double.BitDecrement(half) : half);
        }

        if (typeof(T) == typeof(float))
        {
            float extent = (float)(object)value;
            float half = extent / 2;
            return (T)(object)(half > extent - half ? float.BitDecrement(half) : half);
        }

        if (typeof(T) == typeof(Half))
        {
            var extent = (Half)(object)value;
            Half half = extent / (Half)2;
            return (T)(object)(half > extent - half ? Half.BitDecrement(half) : half);
        }

        return HalfExact(value);
    }

    internal static T HalfExact(T value)
    {
        T two = T.One + T.One;
        T half = value / two;
        if (checked(half + half) != value)
        {
            throw new ArgumentException("The half extent is not representable in the selected scalar type.", nameof(value));
        }

        return half;
    }

    internal static bool Rounded(Rect2D<T> bounds, T radius, Point2D<T> point)
    {
        BigInteger x = Exact(point.X) - Exact(bounds.X);
        BigInteger y = Exact(point.Y) - Exact(bounds.Y);
        BigInteger width = Exact(bounds.Width);
        BigInteger height = Exact(bounds.Height);
        BigInteger r = Exact(radius);
        var dx = BigInteger.Max(r - x, BigInteger.Max(x - (width - r), BigInteger.Zero));
        var dy = BigInteger.Max(r - y, BigInteger.Max(y - (height - r), BigInteger.Zero));
        return (dx * dx) + (dy * dy) <= r * r;
    }
}
