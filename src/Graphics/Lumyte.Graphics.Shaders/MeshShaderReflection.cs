using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shaders;

// Slang's JSON currently erases mesh array types and entry attributes. Read the
// compiler-emitted, specialized HLSL declarations, never application source regexes.
// Unsupported declaration forms fail closed rather than claiming zero requirements.
internal static class MeshShaderReflection
{
    internal static string Complete(string reflection, string declarations, ShaderStage stage, string entryPoint, ShaderTarget target, byte[] code)
    {
        declarations = Regex.Replace(declarations, @"(?m)^[ \t]*#[^\r\n]*|//[^\r\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var structures = Regex.Matches(declarations, @"\bstruct\s+(\w+)\s*\{([^}]+)\}\s*;", RegexOptions.Singleline)
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.Ordinal);
        Match threads = Required(declarations, @"\[numthreads\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)\]", "workgroup size");
        Match entry = Required(declarations, @"\bvoid\s+" + Regex.Escape(entryPoint) + @"\s*\((.*?)\)\s*\{", "entry parameters", RegexOptions.Singleline);
        string parameters = entry.Groups[1].Value;
        string payloadType = string.Empty;
        string payloadName = string.Empty;
        if (stage == ShaderStage.Mesh)
        {
            Match payload = Regex.Match(parameters, @"\b(?:in\s+)?payload\s+(\w+)\s+(\w+)");
            if (payload.Success)
            {
                payloadType = payload.Groups[1].Value;
            }
        }
        else
        {
            Match dispatch = Required(declarations, @"\bDispatchMesh\([^;]*,\s*(\w+)\s*\)\s*;", "Task payload dispatch");
            payloadName = dispatch.Groups[1].Value;
            Match payload = Required(declarations, @"\bgroupshared\s+(\w+)\s+" + Regex.Escape(payloadName) + @"\s*;", "Task payload declaration");
            payloadType = payload.Groups[1].Value;
        }

        (ulong payloadBytes, string payloadAbi, _) = payloadType.Length == 0 ? (0UL, string.Empty, 1u) : Describe(payloadType, structures, [], target);
        ulong shared = 0;
        foreach (Match variable in Regex.Matches(declarations, @"\bgroupshared\s+(\w+)\s+(\w+)\s*(?:\[\s*(?:int\()?\s*(\d+)[uU]?\s*\)?\s*\])?\s*;"))
        {
            if (variable.Groups[2].Value == payloadName)
            {
                continue;
            }

            ulong count = variable.Groups[3].Success ? Parse(variable.Groups[3].Value) : 1;
            (ulong bytes, _, uint alignment) = Describe(variable.Groups[1].Value, structures, [], target);
            shared = checked(Align(shared, alignment) + ((variable.Groups[3].Success ? Align(bytes, alignment) : bytes) * count));
        }

        // Every groupshared declaration must have been understood, including arrays.
        int declared = Regex.Matches(declarations, @"\bgroupshared\b").Count;
        int understood = Regex.Matches(declarations, @"\bgroupshared\s+\w+\s+\w+\s*(?:\[\s*(?:int\()?\s*\d+[uU]?\s*\)?\s*\])?\s*;").Count;
        if (declared != understood)
        {
            throw new InvalidOperationException("Incomplete Mesh/Task reflection: unsupported shared-memory declaration.");
        }

        var metadata = new JsonObject
        {
            ["version"] = 1,
            ["stage"] = stage == ShaderStage.Mesh ? "mesh" : "task",
            ["entryPoint"] = entryPoint,
            ["requiredFeatures"] = (uint)(stage == ShaderStage.Mesh ? GraphicsFeatures.MeshShader : GraphicsFeatures.MeshShader | GraphicsFeatures.TaskShader),
            ["workgroupSize"] = new JsonArray(Parse(threads.Groups[1].Value), Parse(threads.Groups[2].Value), Parse(threads.Groups[3].Value)),
            ["sharedMemoryBytes"] = shared,
            ["payloadBytes"] = payloadBytes,
            ["payloadAbi"] = payloadAbi,
        };
        if (stage == ShaderStage.Mesh)
        {
            Match vertices = Required(parameters, @"\bout\s+vertices\s+(\w+)\s+\w+\s*\[\s*(?:int\()?\s*(\d+)[uU]?\s*\)?\s*\]", "vertex output");
            Match indices = Required(parameters, @"\bout\s+indices\s+(\w+)\s+\w+\s*\[\s*(?:int\()?\s*(\d+)[uU]?\s*\)?\s*\]", "primitive output");
            Match topology = Required(declarations, "\\[outputtopology\\(\\s*\"(line|triangle|point)\"\\s*\\)\\]", "output topology");
            uint vertexCount = Parse(vertices.Groups[2].Value);
            uint primitiveCount = Parse(indices.Groups[2].Value);
            ulong outputBytes = checked(Describe(vertices.Groups[1].Value, structures, [], target).Bytes * vertexCount);
            var outputs = new JsonArray();
            AddOutputs(outputs, vertices.Groups[1].Value, false, structures);
            Match primitives = Regex.Match(parameters, @"\bout\s+primitives\s+(\w+)\s+\w+\s*\[\s*(?:int\()?\s*(\d+)[uU]?\s*\)?\s*\]");
            if (primitives.Success)
            {
                if (Parse(primitives.Groups[2].Value) != primitiveCount)
                {
                    throw new InvalidOperationException("Inconsistent Mesh primitive output counts.");
                }

                outputBytes = checked(outputBytes + (Describe(primitives.Groups[1].Value, structures, [], target).Bytes * primitiveCount));
                AddOutputs(outputs, primitives.Groups[1].Value, true, structures);
            }

            metadata["maxOutputVertices"] = vertexCount;
            metadata["maxOutputPrimitives"] = primitiveCount;
            metadata["outputTopology"] = topology.Groups[1].Value;
            metadata["outputMemoryBytes"] = outputBytes;
            if (target == ShaderTarget.SpirV)
            {
                JsonArray native = MeshSpirVReflection.Outputs(code);
                foreach (JsonNode? output in outputs.Where(output => output!["location"] == null))
                {
                    native.Add(output!.DeepClone());
                }

                outputs = native;
            }

            metadata["outputs"] = outputs;
        }

        JsonObject root = JsonNode.Parse(reflection)!.AsObject();
        root["lumyteMesh"] = metadata;
        return root.ToJsonString();
    }

    internal static string CompleteFragment(string reflection, string declarations, string entryPoint, ShaderTarget target, byte[] code)
    {
        JsonArray inputs;
        if (target == ShaderTarget.SpirV)
        {
            inputs = MeshSpirVReflection.Inputs(code);
        }
        else
        {
            declarations = Regex.Replace(declarations, @"(?m)^[ \t]*#[^\r\n]*|//[^\r\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            var structures = Regex.Matches(declarations, @"\bstruct\s+(\w+)\s*\{([^}]+)\}\s*;", RegexOptions.Singleline)
                .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.Ordinal);
            Match entry = Required(declarations, @"\b\w+\s+" + Regex.Escape(entryPoint) + @"\s*\((.*?)\)", "Fragment entry parameters", RegexOptions.Singleline);
            inputs = [];
            foreach (string parameter in entry.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Regex.IsMatch(parameter, @"^out\s+"))
                {
                    continue;
                }

                string input = Regex.Replace(parameter, @"^(?:in|inout)\s+", string.Empty);
                Match typed = Required(input, @"^(?:(?:in|nointerpolation|linear|noperspective|centroid|sample)\s+)*(\w+)\s+\w+", "Fragment input");
                string type = typed.Groups[1].Value;
                if (structures.ContainsKey(type))
                {
                    AddOutputs(inputs, type, false, structures);
                }
                else
                {
                    structures["__fragmentInput"] = input + ";";
                    AddOutputs(inputs, "__fragmentInput", false, structures);
                }
            }
        }

        JsonObject root = JsonNode.Parse(reflection)!.AsObject();
        root["lumyteFragmentInputs"] = inputs;
        return root.ToJsonString();
    }

    private static void AddOutputs(JsonArray outputs, string type, bool primitive, IReadOnlyDictionary<string, string> structures)
    {
        if (!structures.TryGetValue(type, out string? body))
        {
            throw new InvalidOperationException("Incomplete Mesh reflection: output must have reflected semantic fields.");
        }

        uint location = 0;
        foreach (string declaration in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Match field = Required(declaration, @"^(?:(nointerpolation|linear|noperspective|centroid|sample)\s+)*(\w+)\s+\w+\s*:\s*(\w+)\s*$", "output field");
            string semantic = field.Groups[3].Value.ToUpperInvariant();
            bool builtIn = semantic.StartsWith("SV_", StringComparison.Ordinal);
            string[] modifiers = field.Groups[1].Captures.Select(capture => capture.Value).ToArray();
            string interpolation = modifiers.Contains("nointerpolation") ? "nointerpolation" : modifiers.Contains("noperspective") ? "noperspective" : "linear";
            interpolation += modifiers.Contains("centroid") ? "+centroid" : string.Empty;
            interpolation += modifiers.Contains("sample") ? "+sample" : string.Empty;
            outputs.Add((JsonNode)new JsonObject
            {
                ["semantic"] = semantic,
                ["location"] = builtIn ? null : JsonValue.Create(location++),
                ["type"] = field.Groups[2].Value,
                ["interpolation"] = interpolation,
                ["perPrimitive"] = primitive,
            });
        }
    }

    private static (ulong Bytes, string Abi, uint Alignment) Describe(string type, IReadOnlyDictionary<string, string> structures, HashSet<string> active, ShaderTarget target)
    {
        Match scalar = Regex.Match(type, @"^(uint|int|float|bool)([1-4])?$");
        if (scalar.Success)
        {
            uint lanes = scalar.Groups[2].Success ? Parse(scalar.Groups[2].Value) : 1;
            return (4UL * lanes, scalar.Groups[1].Value + lanes.ToString(CultureInfo.InvariantCulture), target == ShaderTarget.SpirV ? lanes == 3 ? 16u : 4u * lanes : 4u);
        }

        if (!structures.TryGetValue(type, out string? body) || !active.Add(type))
        {
            throw new InvalidOperationException("Incomplete Mesh/Task reflection: unsupported or recursive type " + type);
        }

        ulong bytes = 0;
        uint alignment = 4;
        var fields = new List<string>();
        foreach (string declaration in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Match field = Required(declaration, @"^(?:(?:nointerpolation|linear|noperspective|centroid|sample)\s+)*(\w+)\s+\w+\s*(?:\[\s*(?:int\()?\s*(\d+)[uU]?\s*\)?\s*\])?\s*(?::\s*\w+)?\s*$", "structure field");
            (ulong size, string abi, uint fieldAlignment) = Describe(field.Groups[1].Value, structures, active, target);
            alignment = Math.Max(alignment, fieldAlignment);
            bytes = Align(bytes, fieldAlignment);
            uint count = field.Groups[2].Success ? Parse(field.Groups[2].Value) : 1;
            fields.Add(bytes.ToString(CultureInfo.InvariantCulture) + ":" + abi + "[" + count.ToString(CultureInfo.InvariantCulture) + "]");
            bytes = checked(bytes + ((field.Groups[2].Success ? Align(size, fieldAlignment) : size) * count));
        }

        active.Remove(type);
        bytes = Align(bytes, alignment);
        return (bytes, "{" + string.Join(';', fields) + "}:" + bytes.ToString(CultureInfo.InvariantCulture), alignment);
    }

    private static Match Required(string value, string pattern, string description, RegexOptions options = RegexOptions.None)
    {
        Match match = Regex.Match(value, pattern, options);
        return match.Success ? match : throw new InvalidOperationException("Incomplete Mesh/Task reflection: missing or unsupported " + description + ".");
    }

    private static ulong Align(ulong value, uint alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static uint Parse(string value) => uint.Parse(value, CultureInfo.InvariantCulture);
}
