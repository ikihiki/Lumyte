using System.Numerics;
using Rational = Lumyte.Mathematics.Geometry.GeometryRational;

namespace Lumyte.Mathematics.Colors;

/// <content>Provides transfer, alpha, interpolation, and linear color operations.</content>
public readonly partial record struct Rgba<T>
    where T : INumber<T>
{
    /// <summary>Decodes normalized straight sRGB, preserving alpha.</summary>
    /// <typeparam name="TResult">The explicit result precision.</typeparam>
    /// <returns>The transformed color.</returns>
    public Rgba<TResult> DecodeSrgb<TResult>()
        where TResult : IFloatingPointIeee754<TResult>
    {
        RequireUnitColor(this);

        // Clear historically compares float input against a float threshold, but
        // performs its arithmetic in double. Keep that mixed precision boundary.
        TResult Channel(T c)
        {
            TResult v = TResult.CreateChecked(c);
            bool low = typeof(T) == typeof(float) ? (float)(object)c <= 0.04045f : Rational.From(c).Numerator * Rational.From(0.04045m).Denominator <= Rational.From(0.04045m).Numerator * Rational.From(c).Denominator;
            return FiniteResult(low ? v / TResult.CreateChecked(12.92m) : TResult.Pow((v + TResult.CreateChecked(0.055m)) / TResult.CreateChecked(1.055m), TResult.CreateChecked(2.4m)));
        }

        return new(Channel(R), Channel(G), Channel(B), TResult.CreateChecked(A));
    }

    /// <summary>Encodes normalized straight linear RGB as sRGB, preserving alpha.</summary>
    /// <typeparam name="TResult">The explicit result precision.</typeparam>
    /// <returns>The transformed color.</returns>
    public Rgba<TResult> EncodeSrgb<TResult>()
        where TResult : IFloatingPointIeee754<TResult>
    {
        RequireUnitColor(this);
        TResult Channel(T c)
        {
            TResult v = TResult.CreateChecked(c);
            return FiniteResult(v <= TResult.CreateChecked(0.0031308m) ? v * TResult.CreateChecked(12.92m) : (TResult.CreateChecked(1.055m) * TResult.Pow(v, TResult.One / TResult.CreateChecked(2.4m))) - TResult.CreateChecked(0.055m));
        }

        return new(Channel(R), Channel(G), Channel(B), TResult.CreateChecked(A));
    }

    /// <summary>Associates linear RGB with alpha without clipping HDR components.</summary>
    /// <typeparam name="TResult">The explicit result precision.</typeparam>
    /// <returns>The transformed color.</returns>
    public Rgba<TResult> PremultiplyLinear<TResult>()
        where TResult : INumber<TResult>
    {
        RequireUnit(A);
        if (typeof(T) == typeof(TResult) && (typeof(T) == typeof(float) || typeof(T) == typeof(double) || typeof(T) == typeof(Half)))
        {
            // A single native IEEE product already rounds the exact represented
            // product once. Keep the generic rational path for every other case.
            return new(TResult.CreateChecked(FiniteResult(R * A)), TResult.CreateChecked(FiniteResult(G * A)), TResult.CreateChecked(FiniteResult(B * A)), TResult.CreateChecked(A));
        }

        var alpha = Rational.From(A);
        return new((Rational.From(R) * alpha).To<TResult>(), (Rational.From(G) * alpha).To<TResult>(), (Rational.From(B) * alpha).To<TResult>(), alpha.To<TResult>());
    }

    /// <summary>Unassociates linear RGB; zero alpha becomes transparent black.</summary>
    /// <typeparam name="TResult">The explicit result precision.</typeparam>
    /// <returns>The transformed color.</returns>
    public Rgba<TResult> UnpremultiplyLinear<TResult>()
        where TResult : INumber<TResult>
    {
        RequireUnit(A);
        if (A == T.Zero)
        {
            return new(TResult.Zero, TResult.Zero, TResult.Zero, TResult.Zero);
        }

        if (typeof(T) == typeof(TResult) && (typeof(T) == typeof(float) || typeof(T) == typeof(double) || typeof(T) == typeof(Half)))
        {
            return new(TResult.CreateChecked(FiniteResult(R / A)), TResult.CreateChecked(FiniteResult(G / A)), TResult.CreateChecked(FiniteResult(B / A)), TResult.CreateChecked(A));
        }

        var alpha = Rational.From(A);
        return new((Rational.From(R) / alpha).To<TResult>(), (Rational.From(G) / alpha).To<TResult>(), (Rational.From(B) / alpha).To<TResult>(), alpha.To<TResult>());
    }

    /// <summary>Interpolates normalized sRGB through linear premultiplied components.</summary>
    /// <typeparam name="TResult">The explicit result precision.</typeparam>
    /// <param name="to">The other normalized sRGB endpoint.</param>
    /// <param name="amount">The finite interpolation parameter.</param>
    /// <returns>The transformed color.</returns>
    public Rgba<TResult> InterpolateSrgbLinearPremultiplied<TResult>(Rgba<T> to, TResult amount)
        where TResult : IFloatingPointIeee754<TResult>
    {
        RequireUnitColor(this);
        RequireUnitColor(to);
        if (!TResult.IsFinite(amount))
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (amount == TResult.Zero)
        {
            return ConvertChecked<TResult>();
        }

        if (amount == TResult.One)
        {
            return to.ConvertChecked<TResult>();
        }

        if (this == to)
        {
            return A == T.Zero ? new(TResult.Zero, TResult.Zero, TResult.Zero, TResult.Zero) : ConvertChecked<TResult>();
        }

        Rgba<TResult> first = DecodeSrgb<TResult>();
        Rgba<TResult> last = to.DecodeSrgb<TResult>();
        var t = Rational.From(amount);
        var a = Rational.From(A);
        var b = Rational.From(to.A);
        Rational alpha = a + ((b - a) * t);
        TResult resultAlpha = alpha.To<TResult>();
        RequireUnit(resultAlpha);
        if (alpha.Numerator.Sign < 0 || alpha.Numerator > alpha.Denominator)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "The interpolated alpha is outside the unit interval.");
        }

        if (alpha.Numerator.IsZero)
        {
            return new(TResult.Zero, TResult.Zero, TResult.Zero, TResult.Zero);
        }

        TResult Channel(TResult x, TResult y)
        {
            Rational p = Rational.From(x) * a;
            Rational q = Rational.From(y) * b;
            return ((p + ((q - p) * t)) / alpha).To<TResult>();
        }

        TResult EncodedChannel(T original, T target, TResult decodedFirst, TResult decodedLast)
        {
            if (original == target)
            {
                return TResult.CreateChecked(original);
            }

            // In the linear sRGB toe both transfer factors cancel exactly.
            // Evaluate this identity before decoding to avoid subnormal loss.
            var originalExact = Rational.From(original);
            var targetExact = Rational.From(target);
            var decodeLimit = Rational.From(0.04045m);
            if ((originalExact.Numerator * decodeLimit.Denominator) <= (decodeLimit.Numerator * originalExact.Denominator)
                && (targetExact.Numerator * decodeLimit.Denominator) <= (decodeLimit.Numerator * targetExact.Denominator))
            {
                Rational p = Rational.From(original) * a;
                Rational q = Rational.From(target) * b;
                Rational exact = (p + ((q - p) * t)) / alpha;
                Rational limit = Rational.From(0.0031308m) * Rational.From(12.92m);
                if (exact.Numerator.Sign >= 0 && (exact.Numerator * limit.Denominator) <= (limit.Numerator * exact.Denominator))
                {
                    TResult value = exact.To<TResult>();
                    RequireUnit(value);
                    return value;
                }
            }

            TResult linear = Channel(decodedFirst, decodedLast);
            RequireUnit(linear);
            return FiniteResult(linear <= TResult.CreateChecked(0.0031308m)
                ? linear * TResult.CreateChecked(12.92m)
                : (TResult.CreateChecked(1.055m) * TResult.Pow(linear, TResult.One / TResult.CreateChecked(2.4m))) - TResult.CreateChecked(0.055m));
        }

        var result = new Rgba<TResult>(EncodedChannel(R, to.R, first.R, last.R), EncodedChannel(G, to.G, first.G, last.G), EncodedChannel(B, to.B, first.B, last.B), resultAlpha);
        RequireUnitColor(result);
        return result;
    }

    /// <summary>Composites this linear premultiplied source over a destination.</summary>
    /// <param name="destination">The linear premultiplied destination.</param>
    /// <returns>The transformed color.</returns>
    public Rgba<T> SourceOverLinearPremultiplied(Rgba<T> destination)
    {
        RequireUnit(A);
        RequireUnit(destination.A);
        Rational weight = Rational.From(T.One) - Rational.From(A);
        T Channel(T x, T y) => (Rational.From(x) + (Rational.From(y) * weight)).To<T>();
        return new(Channel(R, destination.R), Channel(G, destination.G), Channel(B, destination.B), Channel(A, destination.A));
    }

    /// <summary>Adds linear premultiplied colors, clipping alpha only.</summary>
    /// <param name="destination">The linear premultiplied destination.</param>
    /// <returns>The transformed color.</returns>
    public Rgba<T> AddLinearPremultiplied(Rgba<T> destination) => AddColor(destination, false);

    /// <summary>Adds linear premultiplied colors, clipping all output components.</summary>
    /// <param name="destination">The linear premultiplied destination.</param>
    /// <returns>The transformed color.</returns>
    public Rgba<T> AddUnitLinearPremultiplied(Rgba<T> destination) => AddColor(destination, true);

    /// <summary>Applies a BCL row-vector straight-linear color transform and associates its result.</summary>
    /// <param name="matrix">The finite BCL row-vector matrix.</param>
    /// <param name="offset">The finite straight-linear bias.</param>
    /// <returns>The transformed color.</returns>
    public Rgba<float> ApplyMatrixLinearPremultiplied(Matrix4x4 matrix, Vector4 offset)
    {
        RequireUnit(A);
        ReadOnlySpan<float> coefficients = [matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44, offset.X, offset.Y, offset.Z, offset.W];
        foreach (float coefficient in coefficients)
        {
            if (!float.IsFinite(coefficient))
            {
                throw new ArgumentOutOfRangeException(nameof(matrix));
            }
        }

        Rgba<float> input = ConvertChecked<float>();
        float alpha = input.A;
        Vector4 straight = alpha > 0 ? new(input.R / alpha, input.G / alpha, input.B / alpha, alpha) : new(0, 0, 0, alpha);
        Vector4 transformed = Vector4.Transform(straight, matrix) + offset;
        float a = Math.Clamp(transformed.W, 0, 1);
        return new(FiniteResult(transformed.X * a), FiniteResult(transformed.Y * a), FiniteResult(transformed.Z * a), FiniteResult(a));
    }

    /// <summary>Computes linear luminance using exact decimal coefficients before final conversion.</summary>
    /// <typeparam name="TResult">The explicit result precision.</typeparam>
    /// <returns>The transformed color.</returns>
    public Rgba<TResult> ToLinearGrayscale<TResult>()
        where TResult : INumber<TResult>
    {
        RequireUnit(A);
        TResult gray = ((Rational.From(R) * Rational.From(0.2126m)) + (Rational.From(G) * Rational.From(0.7152m)) + (Rational.From(B) * Rational.From(0.0722m))).To<TResult>();
        return new(gray, gray, gray, Rational.From(A).To<TResult>());
    }

    private static TValue FiniteResult<TValue>(TValue value)
        where TValue : INumber<TValue> => TValue.IsFinite(value) ? value : throw new OverflowException("The color result is not finite.");

    private static void RequireUnit<TValue>(TValue value)
        where TValue : INumber<TValue>
    {
        if (!TValue.IsFinite(value) || value < TValue.Zero || value > TValue.One)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A normalized component is required.");
        }
    }

    private static void RequireUnitColor<TValue>(Rgba<TValue> value)
        where TValue : INumber<TValue>
    {
        RequireUnit(value.R);
        RequireUnit(value.G);
        RequireUnit(value.B);
        RequireUnit(value.A);
    }

    private Rgba<T> AddColor(Rgba<T> destination, bool clipRgb)
    {
        RequireUnit(A);
        RequireUnit(destination.A);
        T Channel(T x, T y, bool clip)
        {
            Rational sum = Rational.From(x) + Rational.From(y);
            if (clip)
            {
                if (sum.Numerator.Sign < 0)
                {
                    return T.Zero;
                }

                if (sum.Numerator > sum.Denominator)
                {
                    return T.One;
                }
            }

            return sum.To<T>();
        }

        return new(Channel(R, destination.R, clipRgb), Channel(G, destination.G, clipRgb), Channel(B, destination.B, clipRgb), Channel(A, destination.A, true));
    }
}
