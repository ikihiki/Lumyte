using System.Numerics;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics;

internal sealed class ShaderDataLayout
{
    private readonly Dictionary<string, ShaderMember> _members = [];
    private readonly JsonElement _referenceTargets;

    internal ShaderDataLayout(JsonElement type, JsonElement metadata, bool uniform = false)
    {
        _referenceTargets = metadata.TryGetProperty("lumyteReferenceTargets", out JsonElement targets) ? targets.Clone() : default;
        Name = type.GetProperty("name").GetString()!;
        JsonElement storage = type.GetProperty("sizes").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "uniform");
        int size = storage.GetProperty("value").GetInt32();
        int alignment = storage.GetProperty("alignment").GetInt32();
        Size = uniform ? checked(((size + alignment - 1) / alignment) * alignment) : size;
        Add(type, string.Empty, 0);
    }

    internal string Name { get; }

    internal int Size { get; }

    internal static ShaderDataLayout Data(ShaderTargetData target, string name)
    {
        using var document = JsonDocument.Parse(target.ReflectionJson);
        if (!document.RootElement.TryGetProperty("lumyteAbi", out JsonElement abi) || abi.GetInt32() != 1)
        {
            throw new NotSupportedException("The artifact has no supported shader data ABI.");
        }

        JsonElement parameter = document.RootElement.GetProperty("parameters").EnumerateArray().SingleOrDefault(p => p.GetProperty("name").GetString() == "__lumyte_schema_" + name);
        if (parameter.ValueKind == JsonValueKind.Undefined)
        {
            throw new NotSupportedException("The artifact is missing the shader data type schema: " + name);
        }

        return new(parameter.GetProperty("type").GetProperty("resultType"), document.RootElement);
    }

    internal static ShaderTargetData RootTarget(ShaderTargetData vertex, ShaderTargetData? fragment, string name)
    {
        if (HasRoot(vertex, name))
        {
            return vertex;
        }

        if (fragment != null && HasRoot(fragment, name))
        {
            return fragment;
        }

        throw new ArgumentException("The program does not declare this root parameter.");
    }

    internal static void ValidateProgram(ShaderTargetData vertex, ShaderTargetData? fragment)
    {
        if (fragment == null || !HasRoot(vertex) || !HasRoot(fragment))
        {
            return;
        }

        using var document = JsonDocument.Parse(vertex.ReflectionJson);
        string name = document.RootElement.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("type").GetProperty("kind").GetString() == "constantBuffer").GetProperty("name").GetString()!;
        if (!HasRoot(fragment, name) || !Root(vertex, name).Matches(Root(fragment, name)))
        {
            throw new ArgumentException("Vertex and fragment root schemas are incompatible.");
        }
    }

    internal static bool HasRoot(ShaderTargetData target, string? name = null)
    {
        using var document = JsonDocument.Parse(target.ReflectionJson);
        return document.RootElement.GetProperty("parameters").EnumerateArray().Any(p => p.GetProperty("type").GetProperty("kind").GetString() == "constantBuffer" && (name == null || p.GetProperty("name").GetString() == name));
    }

    internal static ShaderDataLayout Root(ShaderTargetData target, string name)
    {
        using var document = JsonDocument.Parse(target.ReflectionJson);
        JsonElement parameter = document.RootElement.GetProperty("parameters").EnumerateArray().SingleOrDefault(p => p.GetProperty("name").GetString() == name);
        if (parameter.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("The program does not declare this root parameter.", nameof(name));
        }

        return new(parameter.GetProperty("type").GetProperty("elementType"), document.RootElement, true);
    }

    internal string ReferenceKind(string path) => _members[path].Kind;

    internal bool Matches(ShaderDataLayout other) => Name == other.Name && Size == other.Size && _members.Count == other._members.Count && _members.All(pair => other._members.TryGetValue(pair.Key, out ShaderMember? member) && pair.Value == member);

    internal void Validate(ShaderValueSnapshot snapshot)
    {
        if (snapshot.ShaderTypeName != Name || snapshot.Values.Count != _members.Count)
        {
            throw new ArgumentException("Shader structure and application codec do not match.");
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (ShaderValue value in snapshot.Values)
        {
            if (!paths.Add(value.Path) || !_members.TryGetValue(value.Path, out ShaderMember? member) || member.Reference != value.IsReference ||
                (!member.Reference && (member.ValueType != value.ValueType || value.Data.Length != member.Size)))
            {
                throw new ArgumentException("Shader member layout and application codec do not match: " + value.Path);
            }

            if (member.Reference && value.Reference is IShaderReference resourceReference)
            {
                object resource = resourceReference.Resource;
                bool supported = member.Kind switch
                {
                    "GpuTextureRef" => resource is IGraphicsTextureView view && view.Info.Dimension == TextureViewDimension.D2,
                    "GpuSamplerRef" => resource is IGraphicsSampler sampler && sampler.Desc.Compare == null,
                    "GpuBufferRef" => resource is IShaderDataSource || (resource is IShaderRawBuffer raw && (raw.Usage & BufferUsage.ShaderRead) != 0),
                    "GpuRWBufferRef" => resource is IShaderRawBuffer raw && (raw.Usage & BufferUsage.ShaderWrite) != 0,
                    _ => false,
                };
                if (!supported)
                {
                    throw new ArgumentException("Reference resource type or access differs from the compiled member: " + value.Path);
                }
            }

            if (member.Target != null && value.Reference != null)
            {
                string logicalType = value.Reference is IShaderReference reference && reference.Resource is IShaderDataSource data ? data.Layout.Name : NumericName(value.ValueType);
                if (logicalType != member.Target)
                {
                    throw new ArgumentException("Buffer reference element type differs from the compiled member: " + value.Path);
                }
            }
        }
    }

    internal byte[] Pack(ShaderValueSnapshot snapshot, Func<ShaderValue, string, byte[]> reference)
    {
        Validate(snapshot);
        byte[] result = new byte[Size];
        foreach (ShaderValue value in snapshot.Values)
        {
            ShaderMember member = _members[value.Path];
            ReadOnlySpan<byte> bytes = member.Reference ? reference(value, member.Kind) : value.Data.Span;
            if (bytes.Length != member.Size)
            {
                throw new ArgumentException("Reference ABI size differs from the compiled layout.");
            }

            bytes.CopyTo(result.AsSpan(member.Offset, member.Size));
        }

        return result;
    }

    private static string NumericName(Type type) => type == typeof(uint) ? "uint" : type == typeof(int) ? "int" : type == typeof(float) ? "float" : type == typeof(Vector2) ? "float2" : type == typeof(Vector3) ? "float3" : type == typeof(Vector4) ? "float4" : type.Name;

    private void Add(JsonElement type, string prefix, int baseOffset)
    {
        foreach (JsonElement field in type.GetProperty("fields").EnumerateArray())
        {
            string path = prefix + field.GetProperty("name").GetString();
            JsonElement memberType = field.GetProperty("type");
            JsonElement binding = field.GetProperty("binding");
            int offset = checked(baseOffset + binding.GetProperty("offset").GetInt32());
            int size = binding.GetProperty("size").GetInt32();
            string kind = memberType.GetProperty("kind").GetString()!;
            string name = memberType.TryGetProperty("name", out JsonElement named) ? named.GetString()! : string.Empty;
            if (name is "GpuTextureRef" or "GpuSamplerRef" or "GpuBufferRef" or "GpuRWBufferRef")
            {
                string? target = _referenceTargets.ValueKind == JsonValueKind.Object && _referenceTargets.TryGetProperty(type.GetProperty("name").GetString() + "." + field.GetProperty("name").GetString(), out JsonElement referenceTarget) ? referenceTarget.GetString() : null;
                if ((name is "GpuBufferRef" or "GpuRWBufferRef") && target == null)
                {
                    throw new NotSupportedException("The artifact is missing a buffer reference target schema.");
                }

                _members.Add(path, new(offset, size, true, name, null, target));
            }
            else if (kind == "struct")
            {
                Add(memberType, path + ".", offset);
            }
            else
            {
                Type valueType = kind switch
                {
                    "scalar" => memberType.GetProperty("scalarType").GetString() switch
                    {
                        "float32" => typeof(float),
                        "int32" => typeof(int),
                        "uint32" => typeof(uint),
                        _ => throw new NotSupportedException("Unsupported shader scalar."),
                    },
                    "vector" when memberType.GetProperty("elementType").GetProperty("scalarType").GetString() == "float32" => memberType.GetProperty("elementCount").GetInt32() switch
                    {
                        2 => typeof(Vector2),
                        3 => typeof(Vector3),
                        4 => typeof(Vector4),
                        _ => throw new NotSupportedException("Unsupported shader vector."),
                    },
                    "matrix" when memberType.GetProperty("rowCount").GetInt32() == 4 && memberType.GetProperty("columnCount").GetInt32() == 4 => typeof(Matrix4x4),
                    _ => throw new NotSupportedException("Unsupported shader member layout."),
                };
                _members.Add(path, new(offset, size, false, kind, valueType, null));
            }
        }
    }

    private sealed record ShaderMember(int Offset, int Size, bool Reference, string Kind, Type? ValueType, string? Target);
}
