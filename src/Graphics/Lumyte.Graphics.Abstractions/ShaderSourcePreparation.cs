using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Prepares the compiler-owned helper ABI and type schema probes consistently offline and online.</summary>
public static class ShaderSourcePreparation
{
    /// <summary>Prepares source only when it explicitly includes the Lumyte shader helpers.</summary>
    /// <param name="source">The application Slang source.</param>
    /// <returns>The source with schema probes and a reserved root binding.</returns>
    public static string Prepare(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source = Regex.Replace(source, @"(?:\[\[vk::binding\([^]]+\)\]\]\s*)?ConstantBuffer\s*<([^>]+)>\s+(\w+)\s*;", "[[vk::binding(8, 0)]] ConstantBuffer<$1> $2;");
        if (!source.Contains("lumyte.slang", StringComparison.Ordinal))
        {
            return source;
        }

        var probes = new StringBuilder();
        foreach (Match type in Regex.Matches(source, @"\bstruct\s+(\w+)\s*\{"))
        {
            string name = type.Groups[1].Value;
            probes.AppendLine($"StructuredBuffer<Ptr<{name}>> __lumyte_schema_{name};");
        }

        // Pointer reflection describes the natural layout used by T* and ByteAddressBuffer.Load<T>.
        // Emit probes first so a same-name root uniform cannot supply its different layout.
        return probes.Append(source).ToString();
    }

    /// <summary>Records the compiler-owned resource ABI version in target metadata.</summary>
    /// <param name="reflection">The compiler reflection JSON.</param>
    /// <param name="source">The source containing logical application structures.</param>
    /// <returns>The complete target schema and helper ABI metadata.</returns>
    public static string CompleteReflection(string reflection, string source)
    {
        JsonObject metadata = JsonNode.Parse(reflection)?.AsObject() ?? throw new ArgumentException("Invalid compiler reflection.", nameof(reflection));
        metadata["lumyteAbi"] = 1;
        metadata["lumyteRootBinding"] = 8;
        var targets = new JsonObject();
        foreach (Match structure in Regex.Matches(source, @"\bstruct\s+(\w+)\s*\{([^}]+)\}", RegexOptions.Singleline))
        {
            foreach (Match field in Regex.Matches(structure.Groups[2].Value, @"Gpu(?:RW)?BufferRef\s*<\s*(\w+)\s*>\s+(\w+)\s*;"))
            {
                targets[structure.Groups[1].Value + "." + field.Groups[2].Value] = field.Groups[1].Value;
            }
        }

        metadata["lumyteReferenceTargets"] = targets;
        return metadata.ToJsonString();
    }
}
