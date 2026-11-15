using System.Globalization;
using System.Numerics;

namespace Lumyte.Mathematics.Sample;

/// <summary>A decimal-backed number with a reference field, deliberately not unmanaged.</summary>
internal readonly struct ManagedDecimal(decimal value) : INumber<ManagedDecimal>
{
    private readonly decimal _value = value;
    private readonly string? _representation = value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets the additive identity.</summary>
    public static ManagedDecimal AdditiveIdentity => Zero;

    /// <summary>Gets the multiplicative identity.</summary>
    public static ManagedDecimal MultiplicativeIdentity => One;

    /// <summary>Gets zero.</summary>
    public static ManagedDecimal Zero => new(0);

    /// <summary>Gets one.</summary>
    public static ManagedDecimal One => new(1);

    /// <summary>Gets the decimal radix.</summary>
    public static int Radix => 10;

    /// <summary>Adds two values.</summary>
    public static ManagedDecimal operator +(ManagedDecimal left, ManagedDecimal right) => new(left._value + right._value);

    /// <summary>Adds two values with overflow checking.</summary>
    public static ManagedDecimal operator checked +(ManagedDecimal left, ManagedDecimal right) => new(checked(left._value + right._value));

    /// <summary>Subtracts two values.</summary>
    public static ManagedDecimal operator -(ManagedDecimal left, ManagedDecimal right) => new(left._value - right._value);

    /// <summary>Subtracts two values with overflow checking.</summary>
    public static ManagedDecimal operator checked -(ManagedDecimal left, ManagedDecimal right) => new(checked(left._value - right._value));

    /// <summary>Multiplies two values.</summary>
    public static ManagedDecimal operator *(ManagedDecimal left, ManagedDecimal right) => new(left._value * right._value);

    /// <summary>Multiplies two values with overflow checking.</summary>
    public static ManagedDecimal operator checked *(ManagedDecimal left, ManagedDecimal right) => new(checked(left._value * right._value));

    /// <summary>Divides two values.</summary>
    public static ManagedDecimal operator /(ManagedDecimal left, ManagedDecimal right) => new(left._value / right._value);

    /// <summary>Divides two values with overflow checking.</summary>
    public static ManagedDecimal operator checked /(ManagedDecimal left, ManagedDecimal right) => new(checked(left._value / right._value));

    /// <summary>Computes the remainder of two values.</summary>
    public static ManagedDecimal operator %(ManagedDecimal left, ManagedDecimal right) => new(left._value % right._value);

    /// <summary>Compares two values.</summary>
    public static bool operator ==(ManagedDecimal left, ManagedDecimal right) => left._value == right._value;

    /// <summary>Compares two values.</summary>
    public static bool operator !=(ManagedDecimal left, ManagedDecimal right) => left._value != right._value;

    /// <summary>Compares two values.</summary>
    public static bool operator <(ManagedDecimal left, ManagedDecimal right) => left._value < right._value;

    /// <summary>Compares two values.</summary>
    public static bool operator <=(ManagedDecimal left, ManagedDecimal right) => left._value <= right._value;

    /// <summary>Compares two values.</summary>
    public static bool operator >(ManagedDecimal left, ManagedDecimal right) => left._value > right._value;

    /// <summary>Compares two values.</summary>
    public static bool operator >=(ManagedDecimal left, ManagedDecimal right) => left._value >= right._value;

    /// <summary>Applies the unary numeric operator.</summary>
    public static ManagedDecimal operator +(ManagedDecimal value) => new(+value._value);

    /// <summary>Applies the unary numeric operator.</summary>
    public static ManagedDecimal operator -(ManagedDecimal value) => new(-value._value);

    /// <summary>Applies the checked unary numeric operator.</summary>
    public static ManagedDecimal operator checked -(ManagedDecimal value) => new(checked(-value._value));

    /// <summary>Applies the unary numeric operator.</summary>
    public static ManagedDecimal operator ++(ManagedDecimal value) => new(value._value + 1);

    /// <summary>Applies the checked unary numeric operator.</summary>
    public static ManagedDecimal operator checked ++(ManagedDecimal value) => new(checked(value._value + 1));

    /// <summary>Applies the unary numeric operator.</summary>
    public static ManagedDecimal operator --(ManagedDecimal value) => new(value._value - 1);

    /// <summary>Applies the checked unary numeric operator.</summary>
    public static ManagedDecimal operator checked --(ManagedDecimal value) => new(checked(value._value - 1));

    /// <summary>Delegates decimal magnitude arithmetic.</summary>
    public static ManagedDecimal Abs(ManagedDecimal x) => new(DecimalAbs(x._value));

    /// <summary>Delegates decimal magnitude arithmetic.</summary>
    public static ManagedDecimal MaxMagnitude(ManagedDecimal x, ManagedDecimal y) => new(DecimalMaxMagnitude(x._value, y._value));

    /// <summary>Delegates decimal magnitude arithmetic.</summary>
    public static ManagedDecimal MaxMagnitudeNumber(ManagedDecimal x, ManagedDecimal y) => new(DecimalMaxMagnitudeNumber(x._value, y._value));

    /// <summary>Delegates decimal magnitude arithmetic.</summary>
    public static ManagedDecimal MinMagnitude(ManagedDecimal x, ManagedDecimal y) => new(DecimalMinMagnitude(x._value, y._value));

    /// <summary>Delegates decimal magnitude arithmetic.</summary>
    public static ManagedDecimal MinMagnitudeNumber(ManagedDecimal x, ManagedDecimal y) => new(DecimalMinMagnitudeNumber(x._value, y._value));

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsCanonical(ManagedDecimal value) => Classify<decimal>(value._value, "IsCanonical");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsComplexNumber(ManagedDecimal value) => Classify<decimal>(value._value, "IsComplexNumber");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsEvenInteger(ManagedDecimal value) => Classify<decimal>(value._value, "IsEvenInteger");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsFinite(ManagedDecimal value) => Classify<decimal>(value._value, "IsFinite");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsImaginaryNumber(ManagedDecimal value) => Classify<decimal>(value._value, "IsImaginaryNumber");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsInfinity(ManagedDecimal value) => Classify<decimal>(value._value, "IsInfinity");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsInteger(ManagedDecimal value) => Classify<decimal>(value._value, "IsInteger");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsNaN(ManagedDecimal value) => Classify<decimal>(value._value, "IsNaN");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsNegative(ManagedDecimal value) => Classify<decimal>(value._value, "IsNegative");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsNegativeInfinity(ManagedDecimal value) => Classify<decimal>(value._value, "IsNegativeInfinity");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsNormal(ManagedDecimal value) => Classify<decimal>(value._value, "IsNormal");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsOddInteger(ManagedDecimal value) => Classify<decimal>(value._value, "IsOddInteger");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsPositive(ManagedDecimal value) => Classify<decimal>(value._value, "IsPositive");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsPositiveInfinity(ManagedDecimal value) => Classify<decimal>(value._value, "IsPositiveInfinity");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsRealNumber(ManagedDecimal value) => Classify<decimal>(value._value, "IsRealNumber");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsSubnormal(ManagedDecimal value) => Classify<decimal>(value._value, "IsSubnormal");

    /// <summary>Delegates decimal classification.</summary>
    public static bool IsZero(ManagedDecimal value) => Classify<decimal>(value._value, "IsZero");

    /// <summary>Parses a decimal representation.</summary>
    public static ManagedDecimal Parse(string s, IFormatProvider? provider) => new(decimal.Parse(s, provider));

    /// <summary>Attempts to parse a decimal representation.</summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out ManagedDecimal result)
    {
        bool success = decimal.TryParse(s, provider, out decimal parsed);
        result = new(parsed);
        return success;
    }

    /// <summary>Parses a decimal representation.</summary>
    public static ManagedDecimal Parse(string s, NumberStyles style, IFormatProvider? provider) => new(decimal.Parse(s, style, provider));

    /// <summary>Attempts to parse a decimal representation.</summary>
    public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out ManagedDecimal result)
    {
        bool success = decimal.TryParse(s, style, provider, out decimal parsed);
        result = new(parsed);
        return success;
    }

    /// <summary>Parses a decimal representation.</summary>
    public static ManagedDecimal Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => new(decimal.Parse(s, provider));

    /// <summary>Attempts to parse a decimal representation.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ManagedDecimal result)
    {
        bool success = decimal.TryParse(s, provider, out decimal parsed);
        result = new(parsed);
        return success;
    }

    /// <summary>Parses a decimal representation.</summary>
    public static ManagedDecimal Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) => new(decimal.Parse(s, style, provider));

    /// <summary>Attempts to parse a decimal representation.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out ManagedDecimal result)
    {
        bool success = decimal.TryParse(s, style, provider, out decimal parsed);
        result = new(parsed);
        return success;
    }

    /// <summary>Creates a value using decimal conversion semantics.</summary>
    public static ManagedDecimal CreateChecked<TOther>(TOther value)
        where TOther : INumberBase<TOther> => value is ManagedDecimal same ? same : new(decimal.CreateChecked(value));

    /// <summary>Converts from another numeric type.</summary>
    static bool INumberBase<ManagedDecimal>.TryConvertFromChecked<TOther>(TOther value, out ManagedDecimal result)
    {
        result = CreateChecked(value);
        return true;
    }

    /// <summary>Converts to another numeric type through decimal.</summary>
    static bool INumberBase<ManagedDecimal>.TryConvertToChecked<TOther>(ManagedDecimal value, out TOther result)
    {
        result = TOther.CreateChecked(value._value);
        return true;
    }

    /// <summary>Creates a value using decimal conversion semantics.</summary>
    public static ManagedDecimal CreateSaturating<TOther>(TOther value)
        where TOther : INumberBase<TOther> => value is ManagedDecimal same ? same : new(decimal.CreateSaturating(value));

    /// <summary>Converts from another numeric type.</summary>
    static bool INumberBase<ManagedDecimal>.TryConvertFromSaturating<TOther>(TOther value, out ManagedDecimal result)
    {
        result = CreateSaturating(value);
        return true;
    }

    /// <summary>Converts to another numeric type through decimal.</summary>
    static bool INumberBase<ManagedDecimal>.TryConvertToSaturating<TOther>(ManagedDecimal value, out TOther result)
    {
        result = TOther.CreateSaturating(value._value);
        return true;
    }

    /// <summary>Creates a value using decimal conversion semantics.</summary>
    public static ManagedDecimal CreateTruncating<TOther>(TOther value)
        where TOther : INumberBase<TOther> => value is ManagedDecimal same ? same : new(decimal.CreateTruncating(value));

    /// <summary>Converts from another numeric type.</summary>
    static bool INumberBase<ManagedDecimal>.TryConvertFromTruncating<TOther>(TOther value, out ManagedDecimal result)
    {
        result = CreateTruncating(value);
        return true;
    }

    /// <summary>Converts to another numeric type through decimal.</summary>
    static bool INumberBase<ManagedDecimal>.TryConvertToTruncating<TOther>(ManagedDecimal value, out TOther result)
    {
        result = TOther.CreateTruncating(value._value);
        return true;
    }

    /// <summary>Compares numeric values.</summary>
    public int CompareTo(ManagedDecimal other) => _value.CompareTo(other._value);

    /// <summary>Compares a boxed numeric value.</summary>
    public int CompareTo(object? obj) => obj is null ? 1 : obj is ManagedDecimal other ? CompareTo(other) : throw new ArgumentException("Expected a managed decimal.", nameof(obj));

    /// <summary>Tests numeric equality independently of representation.</summary>
    public bool Equals(ManagedDecimal other) => _value == other._value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ManagedDecimal other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => _representation ?? "0";

    /// <summary>Formats the underlying decimal.</summary>
    public string ToString(string? format, IFormatProvider? formatProvider) => _value.ToString(format, formatProvider);

    /// <summary>Formats into a character span.</summary>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => _value.TryFormat(destination, out charsWritten, format, provider);

    private static decimal DecimalAbs(decimal x) => decimal.Abs(x);

    private static decimal DecimalMaxMagnitude(decimal x, decimal y) => Magnitude<decimal>(x, y, "MaxMagnitude");

    private static decimal DecimalMaxMagnitudeNumber(decimal x, decimal y) => Magnitude<decimal>(x, y, "MaxMagnitudeNumber");

    private static decimal DecimalMinMagnitude(decimal x, decimal y) => Magnitude<decimal>(x, y, "MinMagnitude");

    private static decimal DecimalMinMagnitudeNumber(decimal x, decimal y) => Magnitude<decimal>(x, y, "MinMagnitudeNumber");

    private static T Magnitude<T>(T x, T y, string operation)
        where T : INumberBase<T> => operation switch
        {
            "MaxMagnitude" => T.MaxMagnitude(x, y),
            "MaxMagnitudeNumber" => T.MaxMagnitudeNumber(x, y),
            "MinMagnitude" => T.MinMagnitude(x, y),
            _ => T.MinMagnitudeNumber(x, y),
        };

    private static bool Classify<T>(T value, string operation)
        where T : INumberBase<T> => operation switch
        {
            "IsCanonical" => T.IsCanonical(value),
            "IsComplexNumber" => T.IsComplexNumber(value),
            "IsEvenInteger" => T.IsEvenInteger(value),
            "IsFinite" => T.IsFinite(value),
            "IsImaginaryNumber" => T.IsImaginaryNumber(value),
            "IsInfinity" => T.IsInfinity(value),
            "IsInteger" => T.IsInteger(value),
            "IsNaN" => T.IsNaN(value),
            "IsNegative" => T.IsNegative(value),
            "IsNegativeInfinity" => T.IsNegativeInfinity(value),
            "IsNormal" => T.IsNormal(value),
            "IsOddInteger" => T.IsOddInteger(value),
            "IsPositive" => T.IsPositive(value),
            "IsPositiveInfinity" => T.IsPositiveInfinity(value),
            "IsRealNumber" => T.IsRealNumber(value),
            "IsSubnormal" => T.IsSubnormal(value),
            "IsZero" => T.IsZero(value),
            _ => throw new ArgumentException("Unknown classification.", nameof(operation)),
        };
}
