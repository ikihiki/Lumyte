using System.Text;
using System.Text.RegularExpressions;

namespace Lumyte.Graphics.Shared;

/// <summary>Expands the compiled WGSL helper ABI to the requested resource counts.</summary>
public static class WgslBindingSpecializer
{
    /// <summary>Rewrites WGSL resource declarations and helper dispatch functions.</summary>
    /// <param name="source">The source input.</param>
    /// <param name="textureCount">The textureCount input.</param>
    /// <param name="samplerCount">The samplerCount input.</param>
    /// <param name="bufferCount">The bufferCount input.</param>
    /// <param name="writableCount">The writableCount input.</param>
    /// <returns>The processed result.</returns>
    public static string Specialize(string source, int textureCount, int samplerCount, int bufferCount, int writableCount)
    {
        source = Expand(source, "lumyteTexture0", "texture_2d<f32>", string.Empty, textureCount, 0, 10);
        source = Expand(source, "lumyteSampler0", "sampler", string.Empty, samplerCount, 1, 10 + Math.Max(0, textureCount - 1));
        source = Expand(source, "lumyteBuffer0", "array<u32>", "<storage, read>", bufferCount, 2, 10 + Math.Max(0, textureCount - 1) + Math.Max(0, samplerCount - 1));
        source = Expand(source, "lumyteRWBuffer0", "array<u32>", "<storage, read_write>", writableCount, 3, 10 + Math.Max(0, textureCount - 1) + Math.Max(0, samplerCount - 1) + Math.Max(0, bufferCount - 1));
        string map = Variable(source, "lumyteMap");
        if (map.Length == 0 && (bufferCount != 0 || writableCount != 0))
        {
            map = "lumyteMap_0";
            source += "\n@binding(9) @group(0) var<storage, read> " + map + " : array<vec4<u32>>;\n";
        }

        foreach (Match function in Regex.Matches(source, @"fn\s+(Gpu(?:RW)?BufferRef_(?:Load|Store)_\w+)\s*\(").Cast<Match>().Reverse())
        {
            int start = source.IndexOf('{', function.Index);
            if (start < 0)
            {
                throw new NotSupportedException("Unknown buffer helper ABI.");
            }

            Match receiver = Regex.Match(source[(function.Index + function.Length)..start], @"^\s*(\w+)\s*:\s*(?:ptr\s*<\s*function\s*,\s*(\w+)\s*>|(\w+))\s*,");
            if (!receiver.Success)
            {
                throw new NotSupportedException("Unknown buffer helper receiver ABI.");
            }

            string argument = receiver.Groups[1].Value;
            bool pointer = receiver.Groups[2].Success;
            string type = receiver.Groups[pointer ? 2 : 3].Value;
            string id = Id(source, type);
            int end = End(source, start);
            string body = source[(start + 1)..end];
            bool writable = function.Groups[1].Value.StartsWith("GpuRW", StringComparison.Ordinal);
            string variable = Variable(source, writable ? "lumyteRWBuffer0" : "lumyteBuffer0");
            if (id.Length == 0 || variable.Length == 0)
            {
                throw new NotSupportedException("Unknown buffer helper resource ABI.");
            }

            string reader = writable ? "lumyteReadWritable" : "lumyteRead";
            string reference = (pointer ? "(*" + argument + ")" : argument) + "." + id;
            if (function.Groups[1].Value.Contains("_Store_", StringComparison.Ordinal))
            {
                body = Regex.Replace(body, Regex.Escape(variable) + @"\[([^\]]+)\]\s*=\s*([^;]+);", m => "lumyteWrite(" + reference + ", " + m.Groups[1].Value + ", " + m.Groups[2].Value + ");");
            }

            body = Regex.Replace(body, Regex.Escape(variable) + @"\[([^\]]+)\]", m => reader + "(" + reference + ", " + m.Groups[1].Value + ")");
            if (Regex.IsMatch(body, @"\b" + Regex.Escape(variable) + @"\b"))
            {
                throw new NotSupportedException("Unknown buffer helper access ABI.");
            }

            source = source[..(start + 1)] + body + source[end..];
        }

        source += Reader(map, "lumyteRead", "lumyteBuffer", bufferCount, "z");
        source += Reader(map, "lumyteReadWritable", "lumyteRWBuffer", writableCount, "w");
        if (writableCount != 0)
        {
            var writer = new StringBuilder("\nfn lumyteWrite(id:u32, index:u32, value:u32) { switch " + map + "[id].w {\n");
            for (int i = 0; i < writableCount; i++)
            {
                writer.Append("case ").Append(i).Append(": { lumyteRWBuffer").Append(i).Append("_0[index] = value; }\n");
            }

            source += writer.Append("default: {}\n} }\n");
        }

        foreach (Match function in Regex.Matches(source, @"fn\s+LumyteSampleGrad_\w+\s*\(([^)]*)\)\s*->\s*vec4<f32>").Cast<Match>().Reverse())
        {
            MatchCollection parameters = Regex.Matches(function.Groups[1].Value, @"(\w+)\s*:\s*(\w+)(?:<[^>]+>)?");
            if (parameters.Count != 5)
            {
                throw new NotSupportedException("Unknown texture helper ABI.");
            }

            string texture = parameters[0].Groups[1].Value;
            string sampler = parameters[1].Groups[1].Value;
            string textureId = Id(source, parameters[0].Groups[2].Value);
            string samplerId = Id(source, parameters[1].Groups[2].Value);
            var body = new StringBuilder("\n switch " + map + "[" + texture + "." + textureId + "].x {\n");
            for (int t = 0; t < textureCount; t++)
            {
                body.Append("case ").Append(t).Append(": { switch ").Append(map).Append('[').Append(sampler).Append('.').Append(samplerId).Append("].y {\n");
                for (int s = 0; s < samplerCount; s++)
                {
                    body.Append("case ").Append(s).Append(": { return textureSampleGrad(lumyteTexture").Append(t).Append("_0, lumyteSampler").Append(s).Append("_0, ").Append(parameters[2].Groups[1].Value).Append(", ").Append(parameters[3].Groups[1].Value).Append(", ").Append(parameters[4].Groups[1].Value).Append("); }\n");
                }

                body.Append("default: {} } }\n");
            }

            body.Append("default: {} }\nreturn vec4<f32>(0.0);\n");
            int start = source.IndexOf('{', function.Index);
            int end = End(source, start);
            source = source[..(start + 1)] + body + source[end..];
        }

        return source;
    }

    private static string Expand(string source, string prefix, string type, string addressSpace, int count, int firstBinding, int extraBinding)
    {
        Match declaration = Regex.Match(source, @"@binding\(\d+\)\s*@group\(0\)\s*var(?:<[^>]+>)?\s+(" + prefix + @"_\w+)\s*:[^;]+;");
        if (declaration.Success)
        {
            source = source.Replace(declaration.Groups[1].Value, prefix + "_0", StringComparison.Ordinal);
            declaration = Regex.Match(source, @"@binding\(\d+\)\s*@group\(0\)\s*var(?:<[^>]+>)?\s+" + prefix + @"_0\s*:[^;]+;");
        }

        var declarations = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            declarations.Append("@binding(").Append(i == 0 ? firstBinding : extraBinding + i - 1).Append(") @group(0) var").Append(addressSpace).Append(' ').Append(prefix.Replace("0", i.ToString(), StringComparison.Ordinal)).Append("_0 : ").Append(type).AppendLine(";");
        }

        return declaration.Success ? source[..declaration.Index] + declarations + source[(declaration.Index + declaration.Length)..] : source + "\n" + declarations;
    }

    private static string Reader(string map, string name, string prefix, int count, string channel)
    {
        if (count == 0)
        {
            return string.Empty;
        }

        var result = new StringBuilder("\nfn " + name + "(id:u32, index:u32)->u32 { switch " + map + "[id]." + channel + " {\n");
        for (int i = 0; i < count; i++)
        {
            result.Append("case ").Append(i).Append(": { return ").Append(prefix).Append(i).Append("_0[index]; }\n");
        }

        return result.Append("default: { return 0u; }\n} }\n").ToString();
    }

    private static string Variable(string source, string prefix) => Regex.Match(source, @"var(?:<[^>]+>)?\s+(" + prefix + @"_\w+)\s*:").Groups[1].Value;

    private static string Id(string source, string type) => Regex.Match(source, @"struct\s+" + Regex.Escape(type) + @"\s*\{[^}]*?\b(Id_\w+)\s*:", RegexOptions.Singleline).Groups[1].Value;

    private static int End(string source, int start)
    {
        int depth = 0;
        for (int i = start; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        throw new ArgumentException("Malformed shader helper function.");
    }
}
