using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Prepares the compiler-owned helper ABI and type schema probes consistently offline and online.</summary>
public static class ShaderSourcePreparation
{
    /// <summary>Prepares logical schemas and opaque wire storage, including helper-free roots.</summary>
    /// <param name="source">The application Slang source.</param>
    /// <returns>The source with schema probes and a reserved root binding.</returns>
    public static string Prepare(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        bool hasRoots = Regex.IsMatch(source, @"\bConstantBuffer\s*<");
        if (!hasRoots && !source.Contains("lumyte.slang", StringComparison.Ordinal))
        {
            return source;
        }

        if (hasRoots && !source.Contains("lumyte.slang", StringComparison.Ordinal))
        {
            source = "#include \"lumyte.slang\"\n" + source;
        }

        // A native aggregate has backend-specific vector/struct alignment.
        // Keep the logical schema for reflection, but read its bytes through
        // portable uint4 lanes (push constants, root constants, or WGSL uniform). No CPU-side numeric repacking is allowed.
        source = Regex.Replace(source, @"(?:\[\[vk::binding\([^]]+\)\]\]\s*)?ConstantBuffer\s*<([^>]+)>\s+(\w+)\s*;", match =>
        {
            string type = match.Groups[1].Value.Trim();
            string name = match.Groups[2].Value;
            return $"\nStructuredBuffer<Ptr<{type}>> __lumyte_schema_root_{name};\n#ifdef LUMYTE_DXIL\nConstantBuffer<LumyteRootWire<{type}>> __lumyte_wire_{name} : register(b8, space0);\n#elif defined(LUMYTE_SPIRV)\n[[vk::push_constant]] ConstantBuffer<LumyteRootWire<{type}>> __lumyte_wire_{name};\n#else\n[[vk::binding(8, 0)]] ConstantBuffer<LumyteRootWire<{type}>> __lumyte_wire_{name};\n#endif\nstatic {type} {name} = __lumyte_wire_{name}.Load();\n";
        });

        // Keep probes in the declaration's preprocessor/namespace scope. Slang
        // resolves forward declarations and pointer layouts before root uniforms.
        return Regex.Replace(source, @"\bstruct\s+(\w+)\s*\{", match =>
            $"StructuredBuffer<Ptr<{match.Groups[1].Value}>> __lumyte_schema_{match.Groups[1].Value};\n" + match.Value);
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

        // Probes beside declarations follow their #if scope and let Slang resolve
        // type expressions (including aliases). Only compiled active wires exist here.
        foreach (JsonObject wire in parameters.OfType<JsonObject>()
            .Where(parameter => parameter["name"]!.GetValue<string>().StartsWith("__lumyte_wire_", StringComparison.Ordinal)))
        {
            string name = wire["name"]!.GetValue<string>()["__lumyte_wire_".Length..];
            JsonObject probe = parameters.OfType<JsonObject>().Single(parameter => parameter["name"]!.GetValue<string>() == "__lumyte_schema_root_" + name);
            JsonNode logical = probe["type"]!["resultType"]!["valueType"]!;
            if (logical is JsonValue)
            {
                string typeName = logical.GetValue<string>();
                logical = FindStructure(parameters, typeName)
                    ?? throw new ArgumentException("Missing logical root schema: " + typeName, nameof(reflection));
            }

            wire["name"] = name;
            wire["type"]!["elementType"] = logical.DeepClone();
        }

        metadata["lumyteReferenceTargets"] = targets;
        return metadata.ToJsonString();
    }

    private static JsonNode? FindStructure(JsonNode node, string name)
    {
        if (node is JsonObject obj)
        {
            if (obj["kind"]?.GetValue<string>() == "struct" && obj["name"]?.GetValue<string>() == name)
            {
                return obj;
            }

            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                if (property.Value is { } value && FindStructure(value, name) is { } found)
                {
                    return found;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? value in array)
            {
                if (value != null && FindStructure(value, name) is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }
}
