using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Lumyte.Mathematics.Colors;

namespace Lumyte.NamedColors.Generator;

internal static class Program
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly string[] _layouts = ["Gray", "GrayAlpha", "Rgb", "Rgba", "Bgr", "Bgra", "Argb", "Abgr"];
    private static readonly Type[] _scalars = [typeof(byte), typeof(ushort), typeof(Half), typeof(float), typeof(double), typeof(decimal)];

    private static int Main(string[] args)
    {
        string root = Directory.GetCurrentDirectory();
        string directory = Path.Combine(root, "tools/colors/Lumyte.NamedColors.Generator");
        string output = Path.Combine(root, "src/Core/Lumyte.Mathematics/Colors/NamedColors");
        bool verify = args.Contains("--verify", StringComparer.Ordinal);
        bool refresh = args.Contains("--refresh-linear-reference", StringComparer.Ordinal);
        if (verify && refresh)
        {
            throw new ArgumentException("Reference refresh and verification are separate operations.");
        }

        ColorRow[] colors = JsonSerializer.Deserialize<ColorRow[]>(File.ReadAllText(Path.Combine(directory, "CanonicalColors.json")), _json)!;
        if (colors.Length != 149 || colors.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 149 || colors.Select(c => c.Field).Distinct(StringComparer.Ordinal).Count() != 149)
        {
            throw new InvalidDataException("Expected 149 unique canonical names and fields.");
        }

        foreach (ColorRow color in colors)
        {
            if (color.Name != color.Field.ToLowerInvariant() || color.Name.Any(c => c is < 'a' or > 'z') || (color.A != 255 && (color.Field != "Transparent" || color.R != 0 || color.G != 0 || color.B != 0 || color.A != 0)))
            {
                throw new InvalidDataException("Invalid canonical CSS row.");
            }
        }

        string referencePath = Path.Combine(directory, "LinearReference.json");
        if (refresh)
        {
            var reference = new LinearReference(
                "b704339b997c128cb0849755ba693f441a8288b0; SRgba<byte>.NormalizeUnorm<float/double>(255).ToLinear<float/double>(); SDK 10.0.403; " + RuntimeInformation.FrameworkDescription + "; " + RuntimeInformation.OSDescription + "; " + RuntimeInformation.ProcessArchitecture,
                Enumerable.Range(0, 256).Select(i => BitConverter.SingleToUInt32Bits(SRgba<byte>.FromRgba8Codes((byte)i, 0, 0, 255).NormalizeUnorm<float>(255).ToLinear<float>().R)).ToArray(),
                Enumerable.Range(0, 256).Select(i => BitConverter.DoubleToUInt64Bits(SRgba<byte>.FromRgba8Codes((byte)i, 0, 0, 255).NormalizeUnorm<double>(255).ToLinear<double>().R)).ToArray());
            File.WriteAllText(referencePath, JsonSerializer.Serialize(reference, _json) + "\n");
        }

        LinearReference frozen = JsonSerializer.Deserialize<LinearReference>(File.ReadAllText(referencePath), _json)!;
        if (frozen.FloatBits.Length != 256 || frozen.DoubleBits.Length != 256 || string.IsNullOrWhiteSpace(frozen.Provenance))
        {
            throw new InvalidDataException("Invalid derived linear reference.");
        }

        if (args.Contains("--verify-native-linear", StringComparer.Ordinal))
        {
            for (int i = 0; i < 256; i++)
            {
                SRgba<byte> code = new((byte)i, 0, 0, 255);
                if (BitConverter.SingleToUInt32Bits(code.NormalizeUnorm<float>(255).ToLinear<float>().R) != frozen.FloatBits[i] || BitConverter.DoubleToUInt64Bits(code.NormalizeUnorm<double>(255).ToLinear<double>().R) != frozen.DoubleBits[i])
                {
                    throw new InvalidDataException($"Native transfer differs at code {i}; frozen output is unchanged.");
                }
            }

            Console.WriteLine("Native float/double transfer: 512 frozen bits matched.");
        }

        Directory.CreateDirectory(output);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var matrix = new StringBuilder("receiver,container,properties,policy\n");
        foreach (string space in new[] { "S", "L" })
        {
            foreach (string layout in _layouts)
            {
                foreach (Type scalar in _scalars.Concat(layout == "Rgba" ? new[] { typeof(sbyte), typeof(short) } : []))
                {
                    MethodInfo method = typeof(Program).GetMethod(nameof(GenerateLayout), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(scalar);
                    var result = (Generated)method.Invoke(null, [space, layout, colors, frozen])!;
                    files.Add(result.Container + ".g.cs", result.Source);
                    matrix.AppendLine($"{result.Receiver},{result.Container},{result.Count},{result.Policy}");
                }
            }
        }

        foreach (Type scalar in new[] { typeof(Half), typeof(float), typeof(double), typeof(decimal) })
        {
            foreach (string model in new[] { "SHsla", "SHsva" })
            {
                var result = (Generated)typeof(Program).GetMethod(nameof(GenerateModel), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(scalar).Invoke(null, [model, colors])!;
                files.Add(result.Container + ".g.cs", result.Source);
                matrix.AppendLine($"{result.Receiver},{result.Container},{result.Count},{result.Policy}");
            }
        }

        foreach (string packed in new[] { "RawRgb565", "RawRgba4444", "RawRgba5551", "RawRgba1010102", "RawBgra1010102" })
        {
            var values = new List<(string Name, string Value)>();
            foreach (ColorRow color in colors.Where(c => packed != "RawRgb565" || c.A == 255))
            {
                SRgba<double> encoded = Codes(color).NormalizeUnorm<double>(255);
                LRgba<double> linear = Linear<double>(color, frozen);
                Type type = typeof(SRgba<>).Assembly.GetType("Lumyte.Mathematics.Colors." + packed)!;
                foreach (string space in new[] { "S", "L" })
                {
                    object value = type.GetMethod(space == "S" ? "FromSrgb" : "FromLinear")!.MakeGenericMethod(typeof(double)).Invoke(null, [space == "S" ? (object)encoded : linear])!;
                    object bits = type.GetProperty("PackedValue")!.GetValue(value)!;
                    values.Add((space + color.Field, $"new({Convert.ToString(bits, CultureInfo.InvariantCulture)}{(bits is uint ? "u" : string.Empty)})"));
                }
            }

            string container = packed + "NamedColorsExtensions";
            files.Add(container + ".g.cs", Source(packed, container, values));
            matrix.AppendLine($"{packed},{container},{values.Count},explicit-S-L-UNORM");
        }

        var rgba8 = colors.Select(c => (c.Field, $"new({c.R}, {c.G}, {c.B}, {c.A})")).ToList();
        files.Add("SRgba8NamedColorsExtensions.g.cs", Source("SRgba8", "SRgba8NamedColorsExtensions", rgba8));
        matrix.AppendLine("SRgba8,SRgba8NamedColorsExtensions,149,UNORM255");
        string catalog = "// <auto-generated/>\nusing System.Collections.Frozen;\n\nnamespace Lumyte.Mathematics.Colors;\n\ninternal static class NamedColorCatalog\n{\n    internal static FrozenDictionary<string, SRgba8> Create() => new Dictionary<string, SRgba8>(StringComparer.OrdinalIgnoreCase)\n    {\n" + string.Join("\n", colors.Select(c => $"        [\"{c.Name}\"] = new({c.R}, {c.G}, {c.B}, {c.A}),")) + "\n    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);\n}\n";
        CheckWrite(Path.Combine(root, "src/Core/Lumyte.Mathematics/Colors/NamedColorCatalog.g.cs"), catalog, verify);
        foreach ((string file, string source) in files)
        {
            CheckWrite(Path.Combine(output, file), source, verify);
        }

        foreach (string path in Directory.EnumerateFiles(output, "*.g.cs").Where(path => !files.ContainsKey(Path.GetFileName(path))))
        {
            throw new InvalidDataException("Unexpected generated source: " + path);
        }

        CheckWrite(Path.Combine(directory, "ReceiverMatrix.csv"), matrix.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), verify);
        Console.WriteLine($"{(verify ? "Verified" : "Generated")} {files.Count} closed receivers; 149 canonical colors.");
        return 0;
    }

    private static Generated GenerateLayout<T>(string space, string layout, ColorRow[] colors, LinearReference reference)
        where T : INumber<T>
    {
        string scalar = ScalarName(typeof(T));
        string receiver = $"{space}{layout}<{scalar}>";
        string container = space + layout + typeof(T).Name + "NamedColorsExtensions";
        bool alpha = layout is "GrayAlpha" or "Rgba" or "Bgra" or "Argb" or "Abgr";
        var values = new List<(string Name, string Value)>();
        foreach (ColorRow color in colors.Where(c => (alpha || c.A == 255) && (!layout.StartsWith("Gray", StringComparison.Ordinal) || (c.R == c.G && c.G == c.B))))
        {
            object rgba;
            if (typeof(T) == typeof(byte) || typeof(T) == typeof(ushort) || typeof(T) == typeof(sbyte) || typeof(T) == typeof(short))
            {
                int max = typeof(T) == typeof(byte) ? 255 : typeof(T) == typeof(ushort) ? 65535 : typeof(T) == typeof(sbyte) ? 127 : 32767;
                if (space == "S")
                {
                    SRgba<int> codes = Codes(color).NormalizeUnorm<double>(255).QuantizeUnorm(max);
                    rgba = new SRgba<T>(T.CreateChecked(codes.R), T.CreateChecked(codes.G), T.CreateChecked(codes.B), T.CreateChecked(codes.A));
                }
                else
                {
                    LRgba<int> codes = Linear<double>(color, reference).QuantizeUnorm(max);
                    rgba = new LRgba<T>(T.CreateChecked(codes.R), T.CreateChecked(codes.G), T.CreateChecked(codes.B), T.CreateChecked(codes.A));
                }
            }
            else
            {
                rgba = space == "S" ? (object)Codes(color).NormalizeUnorm<T>(255) : Linear<T>(color, reference);
            }

            Type target = typeof(SRgba<>).Assembly.GetType("Lumyte.Mathematics.Colors." + space + layout + "`1")!.MakeGenericType(typeof(T));
            object layoutInput = rgba;
            if (!alpha)
            {
                layoutInput = space == "S" ? ((SRgba<T>)rgba).WithAlpha(T.One) : ((LRgba<T>)rgba).WithAlpha(T.One);
            }

            object value = layout == "Rgba" ? rgba : target.GetMethod("FromRgba")!.Invoke(null, [layoutInput])!;
            string[] components = layout switch
            {
                "Gray" => ["Value"],
                "GrayAlpha" => ["Value", "A"],
                "Rgb" => ["R", "G", "B"],
                "Rgba" => ["R", "G", "B", "A"],
                "Bgr" => ["B", "G", "R"],
                "Bgra" => ["B", "G", "R", "A"],
                "Argb" => ["A", "R", "G", "B"],
                _ => ["A", "B", "G", "R"],
            };
            values.Add((color.Field, "new(" + string.Join(", ", components.Select(p => Literal(target.GetProperty(p)!.GetValue(value)!))) + ")"));
        }

        string policy = scalar is "byte" or "ushort" ? "UNORM" : scalar is "sbyte" or "short" ? "positive-SNORM" : "normalized";
        return new(receiver, container, Source(receiver, container, values), values.Count, policy);
    }

    private static Generated GenerateModel<T>(string model, ColorRow[] colors)
        where T : INumber<T>
    {
        string receiver = $"{model}<{ScalarName(typeof(T))}>";
        string container = model + typeof(T).Name + "NamedColorsExtensions";
        var values = new List<(string Name, string Value)>();
        foreach (ColorRow color in colors)
        {
            // Normalize once in decimal, then retain exact kernel intermediates until final model rounding.
            SRgba<decimal> encoded = Codes(color).NormalizeUnorm<decimal>(255);
            object value = model == "SHsla" ? (object)encoded.ToHslSrgbDegrees<T>() : encoded.ToHsvSrgbDegrees<T>();
            string[] properties = ["HueDegrees", "Saturation", model == "SHsla" ? "Lightness" : "Value", "Alpha"];
            values.Add((color.Field, "new(" + string.Join(", ", properties.Select(p => Literal(value.GetType().GetProperty(p)!.GetValue(value)!))) + ")"));
        }

        return new(receiver, container, Source(receiver, container, values), values.Count, "normalized-sRGB-exact-model");
    }

    private static LRgba<T> Linear<T>(ColorRow color, LinearReference reference)
        where T : INumber<T>
    {
        T Channel(byte code) => typeof(T) == typeof(float)
            ? T.CreateChecked(BitConverter.UInt32BitsToSingle(reference.FloatBits[code]))
            : T.CreateChecked(BitConverter.UInt64BitsToDouble(reference.DoubleBits[code]));
        return new(Channel(color.R), Channel(color.G), Channel(color.B), T.CreateChecked(color.A == 0 ? 0 : 1));
    }

    private static SRgba<byte> Codes(ColorRow color) => new(color.R, color.G, color.B, color.A);

    private static string ScalarName(Type type) => type == typeof(Half) ? "Half" : type == typeof(float) ? "float" : type == typeof(double) ? "double" : type == typeof(decimal) ? "decimal" : type == typeof(byte) ? "byte" : type == typeof(ushort) ? "ushort" : type == typeof(sbyte) ? "sbyte" : "short";

    private static string Literal(object value) => value switch
    {
        Half number => $"BitConverter.UInt16BitsToHalf(0x{BitConverter.HalfToUInt16Bits(number):X4})",
        float number => number.ToString("R", CultureInfo.InvariantCulture) + "f",
        double number => number.ToString("R", CultureInfo.InvariantCulture) + "d",
        decimal number => number.ToString(CultureInfo.InvariantCulture) + "m",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
    };

    private static string Source(string receiver, string container, List<(string Name, string Value)> values)
    {
        var result = new StringBuilder("// <auto-generated/>\nnamespace Lumyte.Mathematics.Colors;\n\n");
        result.AppendLine($"/// <summary>Provides precomputed CSS named colors for {receiver.Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)}.</summary>");
        result.AppendLine($"public static class {container}\n{{\n    extension({receiver})\n    {{");
        foreach ((string name, _) in values)
        {
            result.AppendLine($"        /// <summary>Gets the precomputed {name} value.</summary>\n        public static {receiver} {name} => Cache.{name};\n");
        }

        result.AppendLine("    }\n\n    private static class Cache\n    {");
        foreach ((string name, string value) in values)
        {
            result.AppendLine($"        internal static readonly {receiver} {name} = {value};");
        }

        result.AppendLine("    }\n}");
        return result.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void CheckWrite(string path, string content, bool verify)
    {
        if (verify)
        {
            if (!File.Exists(path) || File.ReadAllText(path) != content)
            {
                throw new InvalidDataException("Generated file differs: " + path);
            }
        }
        else
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }

    private sealed record ColorRow(string Name, string Field, byte R, byte G, byte B, byte A);

    private sealed record LinearReference(string Provenance, uint[] FloatBits, ulong[] DoubleBits);

    private sealed record Generated(string Receiver, string Container, string Source, int Count, string Policy);
}
