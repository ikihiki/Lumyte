using System.Numerics;

namespace Lumyte.Mathematics.Geometry;

// Exact intermediate arithmetic for integral and decimal storage. No floating narrowing.
internal readonly struct GeometryRational
{
    internal GeometryRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new DivideByZeroException();
        }

        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = (numerator / gcd) * denominator.Sign;
        Denominator = BigInteger.Abs(denominator / gcd);
    }

    internal BigInteger Numerator { get; }

    internal BigInteger Denominator { get; }

    public static GeometryRational operator +(GeometryRational a, GeometryRational b) => new((a.Numerator * b.Denominator) + (b.Numerator * a.Denominator), a.Denominator * b.Denominator);

    public static GeometryRational operator -(GeometryRational a, GeometryRational b) => new((a.Numerator * b.Denominator) - (b.Numerator * a.Denominator), a.Denominator * b.Denominator);

    public static GeometryRational operator *(GeometryRational a, GeometryRational b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    public static GeometryRational operator /(GeometryRational a, GeometryRational b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);

    internal static bool Supports<T>()
        where T : INumber<T> => typeof(T) == typeof(decimal) || typeof(T) == typeof(BigInteger)
        || typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) || typeof(T) == typeof(short) || typeof(T) == typeof(ushort)
        || typeof(T) == typeof(int) || typeof(T) == typeof(uint) || typeof(T) == typeof(long) || typeof(T) == typeof(ulong)
        || typeof(T) == typeof(nint) || typeof(T) == typeof(nuint) || typeof(T) == typeof(Int128) || typeof(T) == typeof(UInt128);

    internal static GeometryRational From<T>(T value)
        where T : INumber<T>
    {
        if (typeof(T) == typeof(decimal))
        {
            int[] bits = decimal.GetBits((decimal)(object)value);
            BigInteger coefficient = (uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
            return new(bits[3] < 0 ? -coefficient : coefficient, BigInteger.Pow(10, (bits[3] >> 16) & 255));
        }

        if (typeof(T) == typeof(double))
        {
            ulong bits = BitConverter.DoubleToUInt64Bits((double)(object)value);
            return FromBinary(bits, 52, 11, 1023);
        }

        if (typeof(T) == typeof(float))
        {
            return FromBinary(BitConverter.SingleToUInt32Bits((float)(object)value), 23, 8, 127);
        }

        if (typeof(T) == typeof(Half))
        {
            return FromBinary(BitConverter.HalfToUInt16Bits((Half)(object)value), 10, 5, 15);
        }

        if (Supports<T>())
        {
            return new(BigInteger.CreateChecked(value), BigInteger.One);
        }

        // Custom conversion contracts may round even in checked mode. Accept a
        // candidate only when conversion back preserves the original value.
        try
        {
            decimal candidate = decimal.CreateChecked(value);
            if (T.CreateChecked(candidate) == value)
            {
                return From(candidate);
            }
        }
        catch (Exception exception) when (exception is ArithmeticException or NotSupportedException)
        {
            // Decimal may be unsupported or too narrow; try an integral candidate.
        }

        try
        {
            var candidate = BigInteger.CreateChecked(value);
            if (T.CreateChecked(candidate) == value)
            {
                return new(candidate, BigInteger.One);
            }
        }
        catch (Exception exception) when (exception is ArithmeticException or NotSupportedException)
        {
            // Unsupported or lossy conversions cannot supply exact coordinates.
        }

        throw new ArithmeticException("The custom coordinate has no faithful decimal or integral conversion.");
    }

    internal T To<T>()
        where T : INumber<T>
    {
        if (Supports<T>() && typeof(T) != typeof(decimal))
        {
            var integral = BigInteger.DivRem(Numerator, Denominator, out BigInteger remainder);
            if (!remainder.IsZero)
            {
                throw new ArithmeticException("The result is not integral. Select a fractional result type explicitly.");
            }

            return T.CreateChecked(integral);
        }

        if (typeof(T) == typeof(decimal))
        {
            return (T)(object)ToDecimal(false);
        }

        if (typeof(T) == typeof(double))
        {
            return (T)(object)BitConverter.UInt64BitsToDouble(ToBinaryBits(52, 11, 1023));
        }

        if (typeof(T) == typeof(float))
        {
            return (T)(object)BitConverter.UInt32BitsToSingle((uint)ToBinaryBits(23, 8, 127));
        }

        if (typeof(T) == typeof(Half))
        {
            return (T)(object)BitConverter.UInt16BitsToHalf((ushort)ToBinaryBits(10, 5, 15));
        }

        // Split integral and fractional parts so representable quotients do not
        // require their potentially enormous numerator to fit the result type.
        var whole = BigInteger.DivRem(Numerator, Denominator, out BigInteger rest);
        T result = T.CreateChecked(whole);
        if (rest.IsZero)
        {
            return GeometryScalar<T>.Checked(result);
        }

        T numerator = T.CreateChecked(rest);
        T denominator = T.CreateChecked(Denominator);
        T fractionResult = checked(numerator / denominator);
        if (checked(fractionResult * denominator) != numerator)
        {
            throw new ArithmeticException("Division is not exact in the selected result type.");
        }

        return GeometryScalar<T>.Checked(checked(result + fractionResult));
    }

    internal T Root<T>()
        where T : INumber<T>
    {
        if (Numerator.IsZero)
        {
            return T.Zero;
        }

        BigInteger nr = IntegerRoot(Numerator);
        BigInteger dr = IntegerRoot(Denominator);
        if (nr * nr == Numerator && dr * dr == Denominator)
        {
            return new GeometryRational(nr, dr).To<T>();
        }

        if (Supports<T>() && typeof(T) != typeof(decimal))
        {
            throw new ArithmeticException("The distance is not integral. Select a fractional result type explicitly.");
        }

        if (typeof(T) == typeof(decimal))
        {
            return (T)(object)ToDecimal(true);
        }

        // Scale before conversion: squared distances may exceed the result range.
        int exponent = checked((int)(Numerator.GetBitLength() - Denominator.GetBitLength()));
        int scale = exponent / 2;
        GeometryRational normalized = scale >= 0 ? new(Numerator, Denominator << (2 * scale)) : new(Numerator << (-2 * scale), Denominator);
        T root = GeometryScalar<T>.Sqrt(normalized.To<T>());
        if (typeof(T) == typeof(double))
        {
            return GeometryScalar<T>.Checked((T)(object)double.ScaleB((double)(object)root, scale));
        }

        if (typeof(T) == typeof(float))
        {
            return GeometryScalar<T>.Checked((T)(object)float.ScaleB((float)(object)root, scale));
        }

        if (typeof(T) == typeof(Half))
        {
            return GeometryScalar<T>.Checked((T)(object)(Half)float.ScaleB((float)(Half)(object)root, scale));
        }

        T two = T.CreateChecked(2);
        while (scale > 0)
        {
            root = GeometryScalar<T>.Checked(checked(root * two));
            scale--;
        }

        while (scale < 0)
        {
            root /= two;
            scale++;
        }

        return root;
    }

    private static GeometryRational FromBinary(ulong bits, int fractionBits, int exponentBits, int bias)
    {
        ulong exponentMask = (1UL << exponentBits) - 1;
        int exponent = (int)((bits >> fractionBits) & exponentMask);
        if (exponent == (int)exponentMask)
        {
            throw new ArgumentOutOfRangeException(nameof(bits), "A finite coordinate is required.");
        }

        BigInteger significand = bits & ((1UL << fractionBits) - 1);
        if (exponent != 0)
        {
            significand += BigInteger.One << fractionBits;
        }

        if ((bits >> (fractionBits + exponentBits)) != 0)
        {
            significand = -significand;
        }

        int shift = (exponent == 0 ? 1 - bias : exponent - bias) - fractionBits;
        return shift >= 0 ? new(significand << shift, BigInteger.One) : new(significand, BigInteger.One << -shift);
    }

    private static BigInteger IntegerRoot(BigInteger value)
    {
        if (value <= BigInteger.One)
        {
            return value;
        }

        BigInteger root = BigInteger.One << checked((int)((value.GetBitLength() + 1) / 2));
        while (true)
        {
            BigInteger next = (root + (value / root)) >> 1;
            if (next >= root)
            {
                return root;
            }

            root = next;
        }
    }

    // Round the complete exact quotient directly to the target binary lattice.
    // Subnormals share the minimum-normal spacing; no wider floating intermediate
    // is used, so even a perturbation far below double precision breaks ties correctly.
    private ulong ToBinaryBits(int fractionBits, int exponentBits, int bias)
    {
        ulong sign = Numerator.Sign < 0 ? 1UL << (fractionBits + exponentBits) : 0;
        var magnitude = BigInteger.Abs(Numerator);
        if (magnitude.IsZero)
        {
            return sign;
        }

        long exponent = magnitude.GetBitLength() - Denominator.GetBitLength();
        if (exponent > bias + 1L)
        {
            throw new OverflowException("The geometry result is not finite in the selected precision.");
        }

        int minimumExponent = 1 - bias;
        int minimumShift = minimumExponent - fractionBits;
        if (exponent < minimumShift - 1L)
        {
            return sign;
        }

        int binaryExponent = checked((int)exponent);
        if ((binaryExponent >= 0 ? magnitude.CompareTo(Denominator << binaryExponent) : (magnitude << -binaryExponent).CompareTo(Denominator)) < 0)
        {
            binaryExponent--;
        }

        int shift = Math.Max(binaryExponent, minimumExponent) - fractionBits;
        BigInteger scaledNumerator = shift < 0 ? magnitude << -shift : magnitude;
        BigInteger scaledDenominator = shift > 0 ? Denominator << shift : Denominator;
        var significand = BigInteger.DivRem(scaledNumerator, scaledDenominator, out BigInteger remainder);
        int comparison = (2 * remainder).CompareTo(scaledDenominator);
        if (comparison > 0 || (comparison == 0 && !significand.IsEven))
        {
            significand++;
        }

        if (significand == (BigInteger.One << (fractionBits + 1)))
        {
            significand >>= 1;
            binaryExponent++;
        }

        if (binaryExponent > bias)
        {
            throw new OverflowException("The geometry result is not finite in the selected precision.");
        }

        BigInteger hiddenBit = BigInteger.One << fractionBits;
        if (significand < hiddenBit)
        {
            return sign | (ulong)significand;
        }

        ulong encodedExponent = (ulong)(Math.Max(binaryExponent, minimumExponent) + bias);
        return sign | (encodedExponent << fractionBits) | (ulong)(significand - hiddenBit);
    }

    // Select the greatest decimal scale whose rounded coefficient fits 96 bits.
    // Every candidate is rounded directly from the exact rational, never rescaled
    // from a previously rounded value (important near decimal's smallest unit).
    private decimal ToDecimal(bool squareRoot)
    {
        var magnitude = BigInteger.Abs(Numerator);
        BigInteger maxCoefficient = (BigInteger.One << 96) - 1;
        for (int scale = 28; scale >= 0; scale--)
        {
            var factor = BigInteger.Pow(10, scale);
            BigInteger scaled = magnitude * (squareRoot ? factor * factor : factor);
            BigInteger coefficient;
            int comparison;
            if (squareRoot)
            {
                coefficient = IntegerRoot(scaled / Denominator);
                BigInteger midpoint = (2 * coefficient) + 1;
                comparison = (4 * scaled).CompareTo(Denominator * midpoint * midpoint);
            }
            else
            {
                coefficient = BigInteger.DivRem(scaled, Denominator, out BigInteger remainder);
                comparison = (2 * remainder).CompareTo(Denominator);
            }

            if (comparison > 0 || (comparison == 0 && !coefficient.IsEven))
            {
                coefficient++;
            }

            if (coefficient <= maxCoefficient)
            {
                return new decimal((int)(uint)(coefficient & uint.MaxValue), (int)(uint)((coefficient >> 32) & uint.MaxValue), (int)(uint)(coefficient >> 64), Numerator.Sign < 0, (byte)scale);
            }
        }

        throw new OverflowException("The geometry result exceeds decimal range.");
    }
}
