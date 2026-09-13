using System.Text.Json;

namespace Lumyte.Graphics.Native.Shaders.Offline;

internal sealed record NativeHostField(string Name, string Type, uint Offset, uint Size);
internal sealed record NativeReflectedLayout(string Name, uint Size, uint Alignment,
    IReadOnlyList<NativeShaderInputField> Fields, IReadOnlyList<NativeHostField> HostFields)
{
    public string Identity => JsonSerializer.Serialize(new { Name, Size, Alignment, Fields, HostFields });
    public NativeShaderInputLayout ToLayout(string id) => new(id, Size, Alignment, Fields);
}

internal static class NativeShaderReflection
{
    internal static NativeReflectedLayout Root(JsonElement reflection, string? parameterName)
    {
        if (parameterName is null)
        {
            bool declaresRoot = reflection.TryGetProperty("parameters", out JsonElement parameters) &&
                parameters.EnumerateArray().Any(parameter => parameter.TryGetProperty("binding", out JsonElement binding) &&
                    (Text(binding, "kind") == "pushConstantBuffer" ||
                     (Text(binding, "kind") == "constantBuffer" && binding.GetProperty("index").GetUInt32() == 0 &&
                      (!binding.TryGetProperty("space", out JsonElement space) || space.GetUInt32() == 0))));
            bool implicitRoot = reflection.TryGetProperty("globalScope", out JsonElement scope) &&
                scope.TryGetProperty("kind", out JsonElement scopeKind) && scopeKind.GetString() is "constantBuffer" or "pushConstantBuffer";
            if (declaresRoot || implicitRoot)
            {
                throw new InvalidDataException("A root-free build was requested, but Slang reflection declares a direct root. Specify RootParameterName.");
            }
            return new("Root", 0, 1, [], []);
        }
        JsonElement parameter = Find(reflection, parameterName);
        string binding = Text(parameter.GetProperty("binding"), "kind");
        if (binding is not ("pushConstantBuffer" or "constantBuffer"))
        {
            throw new InvalidDataException("The Native root must be a direct push constant / b0 constant buffer declaration.");
        }
        JsonElement location = parameter.GetProperty("binding");
        if (location.GetProperty("index").GetUInt32() != 0 ||
            (location.TryGetProperty("space", out JsonElement space) && space.GetUInt32() != 0))
        {
            throw new InvalidDataException("The Native root declaration must use register b0, space 0.");
        }
        return Read("Root", parameter.GetProperty("type").GetProperty("elementType"));
    }

    internal static NativeReflectedLayout Parameter(JsonElement reflection, string parameterName, string typeName)
        => Read(typeName.Replace("::", "_", StringComparison.Ordinal), Find(reflection, parameterName).GetProperty("type").GetProperty("resultType"));

    private static JsonElement Find(JsonElement reflection, string name)
    {
        foreach (JsonElement parameter in reflection.GetProperty("parameters").EnumerateArray())
        {
            if (Text(parameter, "name") == name) { return parameter; }
        }
        throw new InvalidDataException($"Slang reflection did not contain parameter '{name}'.");
    }

    private static NativeReflectedLayout Read(string name, JsonElement type)
    {
        JsonElement size = UniformSize(type);
        var fields = new List<NativeShaderInputField>();
        var host = new List<NativeHostField>();
        ReadType(type, "", 0, size.GetProperty("value").GetUInt32(), null, fields, host);
        if (fields.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != fields.Count)
        {
            throw new InvalidDataException("Flattened shader field names collide.");
        }
        return new(name, size.GetProperty("value").GetUInt32(), size.GetProperty("alignment").GetUInt32(), fields, host);
    }

    private static void ReadType(JsonElement type, string name, uint offset, uint byteSize, string? resource,
        List<NativeShaderInputField> fields, List<NativeHostField> host)
    {
        string kind = Text(type, "kind");
        if (kind == "struct")
        {
            if (resource is not null) { throw new InvalidDataException("LumyteResource annotates uint or uint64 fields, not structs."); }
            foreach (JsonElement field in type.GetProperty("fields").EnumerateArray())
            {
                string child = Text(field, "name");
                NativeShaderGeneration.Identifier(child);
                JsonElement binding = field.GetProperty("binding");
                if (Text(binding, "kind") != "uniform") { throw new NotSupportedException("Native input layouts contain bytes, not bound resource objects."); }
                ReadType(field.GetProperty("type"), name.Length == 0 ? child : name + "_" + child,
                    checked(offset + binding.GetProperty("offset").GetUInt32()), binding.GetProperty("size").GetUInt32(),
                    Resource(field), fields, host);
            }
            return;
        }
        if (kind == "array")
        {
            uint count = type.GetProperty("elementCount").GetUInt32();
            uint stride = type.GetProperty("uniformStride").GetUInt32();
            JsonElement element = type.GetProperty("elementType");
            uint elementSize = UniformSize(element).GetProperty("value").GetUInt32();
            for (uint i = 0; i < count; i++) { ReadType(element, name + "_" + i, checked(offset + i * stride), elementSize, resource, fields, host); }
            return;
        }
        NativeShaderInputFieldKind fieldKind;
        NativeShaderResourceKind resourceKind = NativeShaderResourceKind.None;
        string hostType;
        if (resource is not null)
        {
            string scalar = kind == "scalar" ? Text(type, "scalarType") : "";
            (fieldKind, resourceKind) = resource switch
            {
                "GpuAddress" when scalar == "uint64" && byteSize == 8 => (NativeShaderInputFieldKind.GpuAddress, NativeShaderResourceKind.Buffer),
                "View" when scalar == "uint32" && byteSize == 4 => (NativeShaderInputFieldKind.DescriptorIndex, NativeShaderResourceKind.View),
                "Sampler" when scalar == "uint32" && byteSize == 4 => (NativeShaderInputFieldKind.DescriptorIndex, NativeShaderResourceKind.Sampler),
                _ => throw new InvalidDataException($"Field '{name}' has an unknown or incompatible LumyteResource annotation '{resource}'."),
            };
            hostType = resourceKind == NativeShaderResourceKind.Buffer ? "ulong" : "uint";
        }
        else
        {
            fieldKind = kind switch { "scalar" => NativeShaderInputFieldKind.Scalar, "vector" => NativeShaderInputFieldKind.Vector,
                "matrix" => NativeShaderInputFieldKind.Matrix, _ => throw new NotSupportedException($"Native reflected type '{kind}' is unsupported.") };
            hostType = kind == "scalar" ? Scalar(Text(type, "scalarType"), byteSize)
                : kind == "vector" && Text(type.GetProperty("elementType"), "scalarType") == "float32" &&
                  type.GetProperty("elementCount").GetUInt32() is >= 2 and <= 4
                    ? "global::System.Numerics.Vector" + type.GetProperty("elementCount").GetUInt32()
                    : "Bytes" + byteSize;
        }
        fields.Add(new(name, fieldKind, offset, byteSize, resourceKind));
        host.Add(new(name, hostType, offset, byteSize));
    }

    private static string? Resource(JsonElement field)
    {
        string? result = null;
        if (field.TryGetProperty("userAttribs", out JsonElement attributes))
        {
            foreach (JsonElement attribute in attributes.EnumerateArray())
            {
                if (Text(attribute, "name") != "LumyteResource") { continue; }
                if (result is not null || attribute.GetProperty("arguments").GetArrayLength() != 1)
                {
                    throw new InvalidDataException("A resource field requires exactly one LumyteResource annotation argument.");
                }
                result = attribute.GetProperty("arguments")[0].GetString() ?? throw new InvalidDataException("Resource annotation must be a string.");
            }
        }
        return result;
    }

    private static string Scalar(string type, uint size) => (type, size) switch
    {
        ("uint8", 1) => "byte", ("int8", 1) => "sbyte", ("uint16", 2) => "ushort", ("int16", 2) => "short",
        ("uint32", 4) => "uint", ("int32", 4) => "int", ("uint64", 8) => "ulong", ("int64", 8) => "long",
        ("float16", 2) => "global::System.Half", ("float32", 4) => "float", ("float64", 8) => "double",
        ("bool", 4) => "uint", _ => throw new NotSupportedException($"No host scalar represents Slang '{type}' of {size} bytes."),
    };

    private static JsonElement UniformSize(JsonElement type)
        => type.GetProperty("sizes").EnumerateArray().Single(s => Text(s, "kind") == "uniform");
    private static string Text(JsonElement value, string property) => value.GetProperty(property).GetString() ?? throw new InvalidDataException($"Missing {property}.");
}
