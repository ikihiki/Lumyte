using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Independent standards-derived receiver matrix and precomputed getter checks.</summary>
public sealed class NamedColorExtensionTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Lazy<JsonElement> _reference = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "tools/colors/Lumyte.NamedColors.Generator/LinearReference.json"))).RootElement.Clone());

    private static JsonElement Reference => _reference.Value;

    /// <summary>Closed receivers, not a generic numeric guess, define the complete supported surface.</summary>
    [Fact]
    public void EveryClosedReceiverHasExactlyItsValidNamesAndValues()
    {
        Dictionary<string, SRgba<byte>> colors = CssColorsTests.CreateExpected();
        Dictionary<Type, string> matrix = ReceiverMatrix();
        Type[] containers = typeof(SRgba<>).Assembly.GetExportedTypes().Where(IsContainer).ToArray();
        Assert.DoesNotContain(typeof(SRgba<>).Assembly.GetExportedTypes(), t => t.IsPublic && t.Name.Contains("NamedColors", StringComparison.Ordinal) && !IsContainer(t));
        Assert.Equal(114, matrix.Count);
        Assert.Equal(matrix.Values.Order(), containers.Select(t => t.Name).Order());
        foreach ((Type receiver, string containerName) in matrix)
        {
            Type container = Assert.Single(containers, t => t.Name == containerName);
            Assert.True(container.IsAbstract && container.IsSealed && !container.IsGenericType);
            Assert.Empty(container.GetFields(Declared));
            MethodInfo[] getters = container.GetMethods(Declared);
            Assert.DoesNotContain(receiver.GetProperties(Declared), p => colors.ContainsKey(p.Name.ToLowerInvariant()));
            Assert.DoesNotContain(receiver.GetFields(Declared), f => colors.ContainsKey(f.Name.ToLowerInvariant()));
            bool raw = receiver.Name.StartsWith("Raw", StringComparison.Ordinal);
            bool gray = receiver.Name.Contains("Gray", StringComparison.Ordinal);
            bool alpha = receiver.Name.Contains("GrayAlpha", StringComparison.Ordinal) || receiver.Name.Contains("Rgba", StringComparison.Ordinal) || receiver.Name.Contains("Bgra", StringComparison.Ordinal) || receiver.Name.Contains("Argb", StringComparison.Ordinal) || receiver.Name.Contains("Abgr", StringComparison.Ordinal) || receiver.Name.StartsWith("SH", StringComparison.Ordinal);
            string[] expected = colors.Where(pair => (alpha || pair.Value.A == 255) && (!gray || (pair.Value.R == pair.Value.G && pair.Value.G == pair.Value.B)))
                .SelectMany(pair => raw ? new[] { "s" + pair.Key, "l" + pair.Key } : new[] { pair.Key }).Order().ToArray();
            Assert.Equal(expected, getters.Select(m => m.Name[4..].ToLowerInvariant()).Order());
            foreach (MethodInfo getter in getters)
            {
                Assert.StartsWith("get_", getter.Name);
                Assert.Equal(receiver, getter.ReturnType);
                Assert.Empty(getter.GetParameters());
                Assert.False(getter.IsGenericMethod);
                AssertReadonlyFieldCopy(getter);
                string name = getter.Name[4..].ToLowerInvariant();
                bool linear = raw ? name[0] == 'l' : receiver.Name[0] == 'L';
                SRgba<byte> codes = colors[raw ? name[1..] : name];
                object actual = getter.Invoke(null, null)!;
                if (raw)
                {
                    Assert.Equal(Packed(receiver.Name, codes, linear), Convert.ToUInt64(receiver.GetProperty("PackedValue")!.GetValue(actual), CultureInfo.InvariantCulture));
                }
                else if (receiver.Name.StartsWith("SH", StringComparison.Ordinal))
                {
                    CheckModel(receiver, actual, codes);
                }
                else
                {
                    Type scalar = receiver == typeof(SRgba8) ? typeof(byte) : receiver.GetGenericArguments()[0];
                    PropertyInfo[] components = receiver.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(p => p.Name is "R" or "G" or "B" or "A" or "Value").ToArray();
                    Assert.Equal(gray ? (alpha ? 2 : 1) : (alpha ? 4 : 3), components.Length);
                    foreach (PropertyInfo property in components)
                    {
                        byte code = property.Name switch { "G" => codes.G, "B" => codes.B, "A" => codes.A, _ => codes.R };
                        Assert.Equal(Component(scalar, code, linear && property.Name != "A"), property.GetValue(actual));
                    }
                }
            }
        }
    }

    /// <summary>Ordinary source syntax resolves actual static extension properties.</summary>
    [Fact]
    public void StaticExtensionSyntaxPreservesCodeAndNormalizedSemantics()
    {
        Assert.Equal(new SRgb<byte>(255, 0, 0), SRgb<byte>.Red);
        Assert.Equal(new SRgba<float>(1, 0, 0, 1), SRgba<float>.Red);
        Assert.Equal(new LRgba<float>(1, 0, 0, 1), LRgba<float>.Red);
        Assert.Equal((sbyte)127, SRgba<sbyte>.Red.R);
        Assert.Equal((short)32767, LRgba<short>.Red.R);
        Assert.Equal((ushort)65535, SRgb<ushort>.Red.R);
        Assert.NotEqual(SRgba<float>.Green, SRgba<float>.Lime);
        Assert.Equal(SRgba<float>.Aqua, SRgba<float>.Cyan);
        Assert.Equal(default, SRgba<float>.Transparent);
        Assert.Equal((ushort)0xf800, RawRgb565.SRed.PackedValue);
    }

    /// <summary>Warm direct getters and ASCII lookup allocate no memory.</summary>
    [Fact]
    public void WarmLookupAndGetterAllocateZeroBytes()
    {
        for (int i = 0; i < 1000; i++)
        {
            _ = SRgba<float>.CornflowerBlue;
            Assert.True(CssColors.TryGet("CoRnFlOwErBlUe", out _));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        float sum = 0;
        bool found = true;
        for (int i = 0; i < 10000; i++)
        {
            sum += SRgba<float>.CornflowerBlue.R;
            found &= CssColors.TryGet("CoRnFlOwErBlUe", out _);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(found);
        Assert.True(sum > 0);
        Assert.Equal(0, allocated);
    }

    internal static bool IsContainer(Type type) => type.IsPublic && type.Namespace == typeof(SRgba<>).Namespace && ReceiverMatrix().Values.Contains(type.Name);

    internal static Dictionary<Type, string> ReceiverMatrix()
    {
        var result = new Dictionary<Type, string>();
        Assembly assembly = typeof(SRgba<>).Assembly;
        Type[] scalars = [typeof(byte), typeof(ushort), typeof(Half), typeof(float), typeof(double), typeof(decimal)];
        foreach (string space in new[] { "S", "L" })
        {
            foreach (string layout in new[] { "Gray", "GrayAlpha", "Rgb", "Rgba", "Bgr", "Bgra", "Argb", "Abgr" })
            {
                foreach (Type scalar in scalars.Concat(layout == "Rgba" ? new[] { typeof(sbyte), typeof(short) } : []))
                {
                    result.Add(assembly.GetType("Lumyte.Mathematics.Colors." + space + layout + "`1")!.MakeGenericType(scalar), space + layout + scalar.Name + "NamedColorsExtensions");
                }
            }
        }

        foreach (string model in new[] { "SHsla", "SHsva" })
        {
            foreach (Type scalar in scalars.Skip(2))
            {
                result.Add(assembly.GetType("Lumyte.Mathematics.Colors." + model + "`1")!.MakeGenericType(scalar), model + scalar.Name + "NamedColorsExtensions");
            }
        }

        foreach (Type raw in new[] { typeof(RawRgb565), typeof(RawRgba4444), typeof(RawRgba5551), typeof(RawRgba1010102), typeof(RawBgra1010102), typeof(SRgba8) })
        {
            result.Add(raw, raw.Name + "NamedColorsExtensions");
        }

        return result;
    }

    private static void AssertReadonlyFieldCopy(MethodInfo getter)
    {
        byte[] il = getter.GetMethodBody()!.GetILAsByteArray()!;

        // Expression getters are field copies; Debug may prefix a nop, never arithmetic or dispatch.
        int offset = il[0] == 0 ? 1 : 0;
        Assert.Equal(offset + 6, il.Length);
        Assert.Equal(0x7e, il[offset]);
        Assert.Equal(0x2a, il[offset + 5]);
        FieldInfo field = getter.Module.ResolveField(BitConverter.ToInt32(il, offset + 1))!;
        Assert.True(field.IsStatic && field.IsInitOnly);
        Assert.Equal(getter.ReturnType, field.FieldType);
        Assert.False(field.DeclaringType!.IsVisible);
        Assert.Equal(getter.DeclaringType, field.DeclaringType.DeclaringType);
    }

    private static object Component(Type scalar, byte code, bool linear)
    {
        double value = linear ? FrozenDouble(code) : code / 255d;
        int max = scalar == typeof(byte) ? 255 : scalar == typeof(ushort) ? 65535 : scalar == typeof(sbyte) ? 127 : 32767;
        if (scalar == typeof(byte) || scalar == typeof(ushort) || scalar == typeof(sbyte) || scalar == typeof(short))
        {
            return Convert.ChangeType(Math.Round(value * max, MidpointRounding.ToEven), scalar, CultureInfo.InvariantCulture);
        }

        if (scalar == typeof(float))
        {
            return linear ? FrozenFloat(code) : code / 255f;
        }

        if (scalar == typeof(Half))
        {
            return (Half)value;
        }

        if (scalar == typeof(decimal))
        {
            return linear ? (decimal)value : code / 255m;
        }

        return value;
    }

    private static ulong Packed(string layout, SRgba<byte> c, bool linear)
    {
        double Channel(byte code) => linear ? FrozenDouble(code) : code / 255d;
        ulong Q(byte code, int bits) => (ulong)Math.Round(Channel(code) * ((1 << bits) - 1), MidpointRounding.ToEven);
        return layout switch
        {
            "RawRgb565" => (Q(c.R, 5) << 11) | (Q(c.G, 6) << 5) | Q(c.B, 5),
            "RawRgba4444" => (Q(c.R, 4) << 12) | (Q(c.G, 4) << 8) | (Q(c.B, 4) << 4) | (c.A == 0 ? 0UL : 15UL),
            "RawRgba5551" => (Q(c.R, 5) << 11) | (Q(c.G, 5) << 6) | (Q(c.B, 5) << 1) | (c.A == 0 ? 0UL : 1UL),
            "RawRgba1010102" => Q(c.R, 10) | (Q(c.G, 10) << 10) | (Q(c.B, 10) << 20) | ((c.A == 0 ? 0UL : 3UL) << 30),
            _ => Q(c.B, 10) | (Q(c.G, 10) << 10) | (Q(c.R, 10) << 20) | ((c.A == 0 ? 0UL : 3UL) << 30),
        };
    }

    private static void CheckModel(Type receiver, object actual, SRgba<byte> c)
    {
        // Decimal inputs share a common exact denominator; all following ratios use independent BigInteger arithmetic.
        var scale = BigInteger.Pow(10, 28);
        BigInteger Channel(byte code)
        {
            int[] bits = decimal.GetBits(code / 255m);
            BigInteger coefficient = (uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
            return coefficient * BigInteger.Pow(10, 28 - ((bits[3] >> 16) & 255));
        }

        BigInteger r = Channel(c.R);
        BigInteger g = Channel(c.G);
        BigInteger b = Channel(c.B);
        var max = BigInteger.Max(r, BigInteger.Max(g, b));
        var min = BigInteger.Min(r, BigInteger.Min(g, b));
        BigInteger delta = max - min;
        BigInteger hue = delta.IsZero ? 0 : 60 * (max == r ? g - b : max == g ? b - r + (2 * delta) : r - g + (4 * delta));
        if (hue.Sign < 0)
        {
            hue += 360 * delta;
        }

        bool hsl = receiver.Name.StartsWith("SHsla", StringComparison.Ordinal);
        (string Name, BigInteger N, BigInteger D)[] components =
        [
            ("HueDegrees", hue, delta.IsZero ? 1 : delta),
            ("Saturation", delta, delta.IsZero ? 1 : hsl ? scale - BigInteger.Abs(max + min - scale) : max),
            (hsl ? "Lightness" : "Value", hsl ? max + min : max, hsl ? 2 * scale : scale),
            ("Alpha", c.A == 0 ? 0 : 1, 1),
        ];
        foreach ((string name, BigInteger numerator, BigInteger denominator) in components)
        {
            object value = receiver.GetProperty(name)!.GetValue(actual)!;
            object expected = value switch
            {
                Half => BitConverter.UInt16BitsToHalf((ushort)BinaryBits(numerator, denominator, 10, 15)),
                float => BitConverter.UInt32BitsToSingle((uint)BinaryBits(numerator, denominator, 23, 127)),
                double => BitConverter.UInt64BitsToDouble(BinaryBits(numerator, denominator, 52, 1023)),
                _ => DecimalRatio(numerator, denominator),
            };
            if (name == "HueDegrees" && (expected is Half half ? (double)half : Convert.ToDouble(expected, CultureInfo.InvariantCulture)) == 360)
            {
                expected = value is Half ? (object)(Half)0 : value is float ? (object)0f : value is double ? (object)0d : 0m;
            }

            Assert.Equal(expected, value);
        }
    }

    private static BigInteger RoundedRatio(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        int comparison = (2 * remainder).CompareTo(denominator);
        return comparison > 0 || (comparison == 0 && !quotient.IsEven) ? quotient + 1 : quotient;
    }

    private static ulong BinaryBits(BigInteger numerator, BigInteger denominator, int fraction, int bias)
    {
        if (numerator.IsZero)
        {
            return 0;
        }

        int exponent = (int)(numerator.GetBitLength() - denominator.GetBitLength());
        if (exponent >= 0 ? numerator < (denominator << exponent) : (numerator << -exponent) < denominator)
        {
            exponent--;
        }

        int shift = fraction - exponent;
        BigInteger mantissa = shift >= 0 ? RoundedRatio(numerator << shift, denominator) : RoundedRatio(numerator, denominator << -shift);
        if (mantissa == (BigInteger.One << (fraction + 1)))
        {
            mantissa >>= 1;
            exponent++;
        }

        return ((ulong)(exponent + bias) << fraction) | (ulong)(mantissa - (BigInteger.One << fraction));
    }

    private static decimal DecimalRatio(BigInteger numerator, BigInteger denominator)
    {
        for (int scale = 28; scale >= 0; scale--)
        {
            BigInteger coefficient = RoundedRatio(numerator * BigInteger.Pow(10, scale), denominator);
            if (coefficient < (BigInteger.One << 96))
            {
                return new decimal((int)(uint)(coefficient & uint.MaxValue), (int)(uint)((coefficient >> 32) & uint.MaxValue), (int)(uint)(coefficient >> 64), false, (byte)scale);
            }
        }

        throw new OverflowException();
    }

    private static double FrozenDouble(byte code) => BitConverter.UInt64BitsToDouble(Reference.GetProperty("DoubleBits")[code].GetUInt64());

    private static float FrozenFloat(byte code) => BitConverter.UInt32BitsToSingle(Reference.GetProperty("FloatBits")[code].GetUInt32());

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lumyte.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository fixture root not found.");
    }
}
