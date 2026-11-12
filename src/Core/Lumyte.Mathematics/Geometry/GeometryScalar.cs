using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

internal static class GeometryScalar<T>
    where T : unmanaged, IFloatingPointIeee754<T>
{
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

    internal static T Checked(T value)
    {
        if (!T.IsFinite(value))
        {
            throw new OverflowException("The geometry result is not finite in the selected precision.");
        }

        return value;
    }

    internal static T Hypot(T x, T y)
    {
        if (typeof(T) == typeof(float))
        {
            float result = new Vector2(float.CreateChecked(x), float.CreateChecked(y)).Length();

            // Squaring subnormal components loses information even when the norm is normal.
            if (float.IsFinite(result) && result > 0 && (x == T.Zero || T.IsNormal(x * x)) && (y == T.Zero || T.IsNormal(y * y)))
            {
                return T.CreateChecked(result);
            }
        }

        return Checked(T.Hypot(x, y));
    }

    internal static T Hypot(T x, T y, T z)
    {
        if (typeof(T) == typeof(float))
        {
            float result = new Vector3(float.CreateChecked(x), float.CreateChecked(y), float.CreateChecked(z)).Length();
            T small = T.Min(T.Abs(x), T.Min(T.Abs(y), T.Abs(z)));
            if (float.IsFinite(result) && result > 0 && (small == T.Zero || T.IsNormal(small * small)))
            {
                // Check every nonzero component, including when an axis is zero.
                if ((x == T.Zero || T.IsNormal(x * x)) && (y == T.Zero || T.IsNormal(y * y)) && (z == T.Zero || T.IsNormal(z * z)))
                {
                    return T.CreateChecked(result);
                }
            }
        }

        return Checked(T.Hypot(T.Hypot(x, y), z));
    }

    internal static (T Numerator, T Denominator, int Exponent) Projection(T dx, T dy, T dz, T px, T py, T pz)
    {
        // Keep product exponents separate so perpendicular coordinates cannot
        // erase a small parallel component before the dot product is formed.
        (T Value, int Exponent) numerator = SumProducts(Product(dx, px), Product(dy, py), Product(dz, pz));
        if (numerator.Value <= T.Zero)
        {
            return (T.Zero, T.One, 0);
        }

        (T Value, int Exponent) denominator = SumProducts(Product(dx, dx), Product(dy, dy), Product(dz, dz));
        if (denominator.Value == T.Zero)
        {
            return (T.Zero, T.One, 0);
        }

        T value = numerator.Value / denominator.Value;
        int exponent = numerator.Exponent - denominator.Exponent;
        return exponent + Exponent(value) >= 0 ? (T.One, T.One, 0) : (numerator.Value, denominator.Value, exponent);
    }

    internal static T Lerp(T start, T end, (T Numerator, T Denominator, int Exponent) amount)
    {
        if (amount.Numerator == T.Zero)
        {
            return start;
        }

        if (amount.Exponent == 0 && amount.Numerator == amount.Denominator)
        {
            return end;
        }

        T parameter = T.ScaleB(amount.Numerator / amount.Denominator, amount.Exponent);
        if (T.IsNormal(parameter))
        {
            // Convex interpolation avoids overflowing a displacement or sum.
            return Checked(((T.One - parameter) * start) + (parameter * end));
        }

        // A subnormal (or zero) parameter can still yield a representable
        // displacement. Multiply mantissas first and apply the combined exponent
        // only once, avoiding both premature underflow and double rounding.
        (T Value, int Exponent) displacement = Product(Difference(end, start), (amount.Numerator, amount.Exponent));
        return Checked(start + T.ScaleB(displacement.Value / amount.Denominator, displacement.Exponent));
    }

    internal static T Orientation(Point2D<T> a, Point2D<T> b, Point2D<T> c)
    {
        (T Value, int Exponent) x = Difference(b.X, a.X);
        (T Value, int Exponent) y = Difference(b.Y, a.Y);
        (T Value, int Exponent) u = Difference(c.X, a.X);
        (T Value, int Exponent) v = Difference(c.Y, a.Y);
        return SumProducts(Product(x, v), Product((-y.Value, y.Exponent), u), (T.Zero, 0)).Value;
    }

    private static (T Value, int Exponent) Difference(T left, T right)
    {
        T difference = left - right;
        if (T.IsFinite(difference))
        {
            return Product(difference, T.One);
        }

        // A predicate needs only the sign; retain overflowing differences in
        // exponent form rather than discarding a small coordinate on another axis.
        return Add(Product(left, T.One), Product(-right, T.One));
    }

    private static (T Value, int Exponent) Product((T Value, int Exponent) left, (T Value, int Exponent) right)
    {
        (T Value, int Exponent) product = Product(left.Value, right.Value);
        return (product.Value, product.Exponent + left.Exponent + right.Exponent);
    }

    // .NET 10 Half.ILogB misreports subnormal exponents. Widening to
    // float is exact and keeps the mantissa normalization finite.
    private static int Exponent(T value) => typeof(T) == typeof(Half) ? float.ILogB(float.CreateChecked(value)) : T.ILogB(value);

    private static (T Value, int Exponent) Product(T left, T right)
    {
        if (left == T.Zero || right == T.Zero)
        {
            return (T.Zero, 0);
        }

        int leftExponent = Exponent(left);
        int rightExponent = Exponent(right);
        T value = T.ScaleB(left, -leftExponent) * T.ScaleB(right, -rightExponent);
        return (value, leftExponent + rightExponent);
    }

    private static (T Value, int Exponent) SumProducts((T Value, int Exponent) first, (T Value, int Exponent) second, (T Value, int Exponent) third)
    {
        // Cancel the largest terms first, exposing a much smaller third term.
        if (MagnitudeLess(first, second))
        {
            (first, second) = (second, first);
        }

        if (MagnitudeLess(first, third))
        {
            (first, third) = (third, first);
        }

        if (MagnitudeLess(second, third))
        {
            (second, third) = (third, second);
        }

        return Add(Add(first, second), third);
    }

    private static bool MagnitudeLess((T Value, int Exponent) left, (T Value, int Exponent) right) => left.Value == T.Zero
        || (right.Value != T.Zero && (left.Exponent < right.Exponent || (left.Exponent == right.Exponent && T.Abs(left.Value) < T.Abs(right.Value))));

    private static (T Value, int Exponent) Add((T Value, int Exponent) left, (T Value, int Exponent) right)
    {
        if (left.Value == T.Zero)
        {
            return right;
        }

        if (right.Value == T.Zero)
        {
            return left;
        }

        int exponent = Math.Max(left.Exponent, right.Exponent);
        T value = T.ScaleB(left.Value, left.Exponent - exponent) + T.ScaleB(right.Value, right.Exponent - exponent);
        if (value == T.Zero)
        {
            return (T.Zero, 0);
        }

        int adjustment = Exponent(value);
        return (T.ScaleB(value, -adjustment), exponent + adjustment);
    }
}
