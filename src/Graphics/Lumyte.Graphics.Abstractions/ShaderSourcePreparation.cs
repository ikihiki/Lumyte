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
        if (!source.Contains("lumyte.slang", StringComparison.Ordinal))
        {
            return Regex.Replace(source, @"(?:\[\[vk::binding\([^]]+\)\]\]\s*)?ConstantBuffer\s*<([^>]+)>\s+(\w+)\s*;", "\n#ifdef LUMYTE_DXIL\nConstantBuffer<$1> $2 : register(b8, space0);\n#elif defined(LUMYTE_SPIRV)\n[[vk::push_constant]] ConstantBuffer<$1> $2;\n#else\n[[vk::binding(8, 0)]] ConstantBuffer<$1> $2;\n#endif\n");
        }

        // A native aggregate has backend-specific vector/struct alignment.
        // Keep the logical schema for reflection, but read its bytes through
        // portable uint4 lanes (push constants, root constants, or WGSL uniform). No CPU-side numeric repacking is allowed.
        source = Regex.Replace(source, @"(?:\[\[vk::binding\([^]]+\)\]\]\s*)?ConstantBuffer\s*<([^>]+)>\s+(\w+)\s*;", match =>
        {
            string type = match.Groups[1].Value.Trim();
            string name = match.Groups[2].Value;
            return $"\n#ifdef LUMYTE_DXIL\nConstantBuffer<LumyteRootWire<{type}>> __lumyte_wire_{name} : register(b8, space0);\n#elif defined(LUMYTE_SPIRV)\n[[vk::push_constant]] ConstantBuffer<LumyteRootWire<{type}>> __lumyte_wire_{name};\n#else\n[[vk::binding(8, 0)]] ConstantBuffer<LumyteRootWire<{type}>> __lumyte_wire_{name};\n#endif\nstatic {type} {name} = __lumyte_wire_{name}.Load();\n";
        });

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
    /// <param name="target">The compiled target, when known. Vulkan push storage is also detected from reflection.</param>
    /// <returns>The complete target schema and helper ABI metadata.</returns>
    public static string CompleteReflection(string reflection, string source, ShaderTarget? target = null)
    {
        JsonObject metadata = JsonNode.Parse(reflection)?.AsObject() ?? throw new ArgumentException("Invalid compiler reflection.", nameof(reflection));
        metadata["lumyteAbi"] = 1;
        metadata["lumyteRootBinding"] = 8;

        // Storage is independent of the 16-byte resource-reference ABI. Old Vulkan
        // descriptor-backed artifacts must never pass as native push constants.
        bool pushConstants = metadata["parameters"]!.AsArray().OfType<JsonObject>()
            .Any(parameter => parameter["binding"]?["kind"]?.GetValue<string>() == "pushConstantBuffer");
        metadata["lumyteRootStorage"] = pushConstants ? "VkPushConstants"
            : target == ShaderTarget.Dxil ? "D3DRoot32BitConstants" : "UniformBuffer";
        var targets = new JsonObject();
        foreach (Match structure in Regex.Matches(source, @"\bstruct\s+(\w+)\s*\{([^}]+)\}", RegexOptions.Singleline))
        {
            foreach (Match field in Regex.Matches(structure.Groups[2].Value, @"Gpu(?:RW)?BufferRef\s*<\s*(\w+)\s*>\s+(\w+)\s*;"))
            {
                targets[structure.Groups[1].Value + "." + field.Groups[2].Value] = field.Groups[1].Value;
            }
        }

        // Reflection exposes the logical scalar schema while the compiled binding
        // is opaque words. Its declaration rounds up to 16 bytes, not its native count;
        // TransferSize remains the exact C# size and is never rounded/repacked.
        JsonArray parameters = metadata["parameters"]!.AsArray();
        if (source.Contains("lumyte.slang", StringComparison.Ordinal))
        {
            foreach (Match root in Regex.Matches(source, @"ConstantBuffer\s*<([^>]+)>\s+(\w+)\s*;"))
            {
                string name = root.Groups[2].Value;
                JsonObject wire = parameters.OfType<JsonObject>().Single(p => p["name"]!.GetValue<string>() == "__lumyte_wire_" + name);
                JsonObject probe = parameters.OfType<JsonObject>().Single(p => p["name"]!.GetValue<string>() == "__lumyte_schema_" + root.Groups[1].Value.Trim());
                wire["name"] = name;
                wire["type"]!["elementType"] = probe["type"]!["resultType"]!["valueType"]!.DeepClone();
            }
        }

        metadata["lumyteReferenceTargets"] = targets;
        return metadata.ToJsonString();
    }
}
