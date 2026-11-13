using System.Numerics;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Reads and packs compiled shader structure layouts.</summary>
public sealed partial class ShaderDataLayout : IShaderDataLayout
{
    private readonly Dictionary<string, ShaderMember> _members = [];
    private readonly JsonElement _referenceTargets;

    private ShaderDataLayout(JsonElement type, JsonElement metadata)
    {
        _referenceTargets = metadata.TryGetProperty("lumyteReferenceTargets", out JsonElement targets) ? targets.Clone() : default;
        Name = type.GetProperty("name").GetString()!;
        JsonElement storage = type.GetProperty("sizes").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "uniform");
        int size = storage.GetProperty("value").GetInt32();
        Size = size;
        Add(type, string.Empty, 0);
    }

    private ShaderDataLayout(ShaderDataLayout compiled, int size)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        Name = compiled.Name;
        Size = size;
        _members = compiled._members;
        _referenceTargets = compiled._referenceTargets;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public int Size { get; }

    /// <summary>Provides the packed shader data allocations.</summary>
    /// <param name="target">The target input.</param>
    /// <param name="name">The name input.</param>
    /// <returns>The processed result.</returns>
    public static ShaderDataLayout Data(ShaderTargetData target, string name)
        => GetReflection(target).Data(name);

    /// <summary>Gets a cached data layout without repeatedly extracting the artifact target.</summary>
    /// <param name="artifact">The immutable shader artifact owning the cache lifetime.</param>
    /// <param name="target">The backend code target.</param>
    /// <param name="name">The shader data structure name.</param>
    /// <returns>The immutable layout shared by buffers using this artifact and target.</returns>
    public static ShaderDataLayout Data(ShaderArtifact artifact, ShaderTarget target, string name)
        => _artifacts.GetValue(artifact, static _ => new()).Data(artifact, target, name);

    /// <summary>Selects the shader stage declaring a root parameter.</summary>
    /// <param name="vertex">The vertex input.</param>
    /// <param name="fragment">The fragment input.</param>
    /// <param name="name">The name input.</param>
    /// <returns>The processed result.</returns>
    public static ShaderTargetData RootTarget(ShaderTargetData vertex, ShaderTargetData? fragment, string name)
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

    /// <summary>Validates compatible root and transitively referenced layouts across graphics stages.</summary>
    /// <param name="vertex">The vertex input.</param>
    /// <param name="fragment">The fragment input.</param>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void ValidateProgram(ShaderTargetData vertex, ShaderTargetData? fragment)
    {
        if (fragment == null || !HasRoot(vertex) || !HasRoot(fragment))
        {
            return;
        }

        using var document = JsonDocument.Parse(vertex.ReflectionJson);
        string name = document.RootElement.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("type").GetProperty("kind").GetString() == "constantBuffer" && p.GetProperty("name").GetString() != "lumyteDrawInfo").GetProperty("name").GetString()!;
        ShaderDataLayout root = Root(vertex, name);
        if (!HasRoot(fragment, name) || !root.Matches(Root(fragment, name)))
        {
            throw new ArgumentException("Vertex and fragment root schemas are incompatible.");
        }

        using var fragmentDocument = JsonDocument.Parse(fragment.ReflectionJson);
        var pending = new Stack<string>(root._members.Values.Where(member => member.Target != null).Select(member => member.Target!));
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out string? target))
        {
            if (!visited.Add(target))
            {
                continue;
            }

            JsonElement vertexType = DataType(document.RootElement, target);
            JsonElement fragmentType = DataType(fragmentDocument.RootElement, target);
            if (vertexType.ValueKind == JsonValueKind.Undefined && fragmentType.ValueKind == JsonValueKind.Undefined)
            {
                // Intrinsic raw buffer element types have no application structure probe.
                if (target is "int" or "uint" or "float" or "float2" or "float3" or "float4" or "float4x4")
                {
                    continue;
                }

                throw new NotSupportedException("The artifact is missing the referenced shader data layout: " + target);
            }

            if (vertexType.ValueKind == JsonValueKind.Undefined || fragmentType.ValueKind == JsonValueKind.Undefined || !JsonElement.DeepEquals(vertexType, fragmentType))
            {
                throw new ArgumentException("Vertex and fragment referenced schemas are incompatible: " + target);
            }

            foreach ((string field, string referenceTarget) in BufferReferenceTargets(vertexType, document.RootElement))
            {
                if (!fragmentDocument.RootElement.GetProperty("lumyteReferenceTargets").TryGetProperty(field, out JsonElement otherTarget) || otherTarget.GetString() != referenceTarget)
                {
                    throw new ArgumentException("Vertex and fragment reference targets are incompatible: " + field);
                }

                pending.Push(referenceTarget);
            }
        }
    }

    /// <summary>Checks whether a shader declares a root constant buffer.</summary>
    /// <param name="target">The target input.</param>
    /// <param name="name">The name input.</param>
    /// <returns>The processed result.</returns>
    public static bool HasRoot(ShaderTargetData target, string? name = null)
        => GetReflection(target).HasRoot(name);

    /// <summary>Provides the immutable root values.</summary>
    /// <param name="target">The target input.</param>
    /// <param name="name">The name input.</param>
    /// <returns>The processed result.</returns>
    public static ShaderDataLayout Root(ShaderTargetData target, string name)
        => GetReflection(target).Root(name);

    /// <summary>Uses the static C# wire size as the storage stride without constructing a value.</summary>
    /// <typeparam name="T">The application transfer type.</typeparam>
    /// <param name="artifact">The compiled shader artifact.</param>
    /// <param name="target">The active backend target.</param>
    /// <returns>The C#-stride storage layout.</returns>
    public static ShaderDataLayout Data<T>(ShaderArtifact artifact, ShaderTarget target)
        where T : IShaderData
    {
        ShaderDataLayout compiled = Data(artifact, target, T.ShaderTypeName);
        compiled.ValidateTransfer(T.TransferSize, T.TransferFields, T.ShaderTypeName);
        return new(compiled, T.TransferSize);
    }

    /// <summary>Validates C# layout in Debug and native resource admission in every configuration.</summary>
    /// <param name="snapshot">The captured raw wire value.</param>
    /// <param name="validateReferences">Whether native references must resolve to compatible resources.</param>
    public void ValidateArguments(ShaderValueSnapshot snapshot, bool validateReferences = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateTransfer(snapshot.Data.Length, snapshot.Fields, snapshot.ShaderTypeName);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if ((validateReferences && snapshot.ShaderTypeName != Name) || snapshot.Values.Count != _members.Values.Count(member => member.Reference))
        {
            throw new ArgumentException("Root type or resource count differs from the compiled schema.", nameof(snapshot));
        }

        foreach (ShaderValue value in snapshot.Values)
        {
            if (!seen.Add(value.Path) || !_members.TryGetValue(value.Path, out ShaderMember? member) || !member.Reference || value.Offset < 0 || value.Offset > snapshot.Data.Length - 16 || (validateReferences && !ReferenceMatches(value, member)))
            {
                throw new ArgumentException("Resource kind, element type, access or slot differs from the compiled schema.", nameof(snapshot));
            }
        }
    }

    /// <inheritdoc/>
    public string ReferenceKind(string path)
    {
        if (!_members.TryGetValue(path, out ShaderMember? member) || !member.Reference)
        {
            throw new ArgumentException("The shader does not declare this resource reference.", nameof(path));
        }

        return member.Kind;
    }

    /// <inheritdoc/>
    public bool Matches(IShaderDataLayout layout) => layout is ShaderDataLayout other && Name == other.Name && Size == other.Size && _members.Count == other._members.Count && _members.All(pair => other._members.TryGetValue(pair.Key, out ShaderMember? member) && pair.Value == member);

    /// <inheritdoc/>
    public byte[] Pack(ShaderValueSnapshot snapshot, Func<ShaderValue, string, byte[]> reference)
    {
        ValidateArguments(snapshot);
        byte[] result = snapshot.Data.ToArray();
        foreach (ShaderValue value in snapshot.Values)
        {
            // Reference kinds remain operational metadata. Numeric placement never comes from reflection.
            if (value.Offset < 0 || value.Offset > result.Length - 16)
            {
                throw new ArgumentException("Reference slot exceeds the C# wire value.");
            }

            string kind = ReferenceKind(value.Path);
            byte[] bytes = reference(value, kind);
            if (bytes.Length != 16)
            {
                throw new ArgumentException("Reference encoding must match the 16-byte ABI.");
            }

            bytes.CopyTo(result.AsSpan(value.Offset, 16));
        }

        return result;
    }

    private static bool ReferenceMatches(ShaderValue value, ShaderMember member)
    {
        if (value.Reference is not IShaderReference reference)
        {
            return false;
        }

        return member.Kind switch
        {
            "GpuTextureRef" => value.ValueType == typeof(IGraphicsTextureView) && reference.Resource is IGraphicsTextureView,
            "GpuSamplerRef" => value.ValueType == typeof(IGraphicsSampler) && reference.Resource is IGraphicsSampler,
            "GpuBufferRef" when reference.Resource is IShaderDataSource data => data.Layout.Name == member.Target && data.Memory == MemoryPreference.Automatic,
            "GpuBufferRef" or "GpuRWBufferRef" => RawReferenceMatches(value, reference.Resource, member),
            _ => false,
        };
    }

    private static bool RawReferenceMatches(ShaderValue value, object resource, ShaderMember member)
    {
        string? target = value.ValueType == typeof(int) ? "int"
            : value.ValueType == typeof(uint) ? "uint"
            : value.ValueType == typeof(float) ? "float"
            : value.ValueType == typeof(Vector2) ? "float2"
            : value.ValueType == typeof(Vector3) ? "float3"
            : value.ValueType == typeof(Vector4) ? "float4"
            : value.ValueType == typeof(Matrix4x4) ? "float4x4" : null;
        BufferUsage? usage = resource switch
        {
            IGraphicsBuffer<int> buffer when value.ValueType == typeof(int) => buffer.Usage,
            IGraphicsBuffer<uint> buffer when value.ValueType == typeof(uint) => buffer.Usage,
            IGraphicsBuffer<float> buffer when value.ValueType == typeof(float) => buffer.Usage,
            IGraphicsBuffer<Vector2> buffer when value.ValueType == typeof(Vector2) => buffer.Usage,
            IGraphicsBuffer<Vector3> buffer when value.ValueType == typeof(Vector3) => buffer.Usage,
            IGraphicsBuffer<Vector4> buffer when value.ValueType == typeof(Vector4) => buffer.Usage,
            IGraphicsBuffer<Matrix4x4> buffer when value.ValueType == typeof(Matrix4x4) => buffer.Usage,
            _ => null,
        };
        BufferUsage required = member.Kind == "GpuRWBufferRef" ? BufferUsage.ShaderWrite : BufferUsage.ShaderRead;
        return target is not null && target == member.Target && usage.HasValue && (usage.Value & required) == required;
    }

    private static JsonElement DataType(JsonElement metadata, string name)
    {
        JsonElement parameter = metadata.GetProperty("parameters").EnumerateArray().SingleOrDefault(p => p.GetProperty("name").GetString() == "__lumyte_schema_" + name);
        if (parameter.ValueKind == JsonValueKind.Undefined)
        {
            return default;
        }

        JsonElement pointer = parameter.GetProperty("type").GetProperty("resultType");
        if (pointer.GetProperty("kind").GetString() != "pointer")
        {
            throw new NotSupportedException("The artifact is missing a natural shader data layout: " + name);
        }

        JsonElement type = pointer.GetProperty("valueType");
        if (type.ValueKind == JsonValueKind.Object)
        {
            return type;
        }

        if (type.ValueKind == JsonValueKind.String)
        {
            foreach (JsonElement probe in metadata.GetProperty("parameters").EnumerateArray().Where(p => p.GetProperty("name").GetString()!.StartsWith("__lumyte_schema_", StringComparison.Ordinal)))
            {
                JsonElement candidate = FindStructure(probe.GetProperty("type").GetProperty("resultType"), type.GetString()!);
                if (candidate.ValueKind != JsonValueKind.Undefined)
                {
                    return candidate;
                }
            }
        }

        throw new NotSupportedException("The artifact is missing the referenced shader data layout: " + name);
    }

    private static JsonElement FindStructure(JsonElement node, string name)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("kind", out JsonElement kind) && kind.GetString() == "struct" && node.GetProperty("name").GetString() == name)
            {
                return node;
            }

            foreach (JsonProperty property in node.EnumerateObject())
            {
                JsonElement found = FindStructure(property.Value, name);
                if (found.ValueKind != JsonValueKind.Undefined)
                {
                    return found;
                }
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in node.EnumerateArray())
            {
                JsonElement found = FindStructure(element, name);
                if (found.ValueKind != JsonValueKind.Undefined)
                {
                    return found;
                }
            }
        }

        return default;
    }

    private static IEnumerable<(string Field, string Target)> BufferReferenceTargets(JsonElement type, JsonElement metadata)
    {
        if (!type.TryGetProperty("fields", out JsonElement fields))
        {
            yield break;
        }

        foreach (JsonElement field in fields.EnumerateArray())
        {
            JsonElement memberType = field.GetProperty("type");
            if (memberType.TryGetProperty("name", out JsonElement named) && named.GetString() is "GpuBufferRef" or "GpuRWBufferRef")
            {
                string path = type.GetProperty("name").GetString() + "." + field.GetProperty("name").GetString();
                if (!metadata.GetProperty("lumyteReferenceTargets").TryGetProperty(path, out JsonElement target))
                {
                    throw new NotSupportedException("The artifact is missing a buffer reference target schema: " + path);
                }

                yield return (path, target.GetString()!);
            }
            else
            {
                foreach ((string path, string target) in BufferReferenceTargets(memberType, metadata))
                {
                    yield return (path, target);
                }
            }
        }
    }

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
#if DEBUG
                Type valueType = kind switch
                {
                    "scalar" => memberType.GetProperty("scalarType").GetString() switch
                    {
                        "float16" => typeof(Half),
                        "float32" => typeof(float),
                        "float64" => typeof(double),
                        "int8" => typeof(sbyte),
                        "uint8" => typeof(byte),
                        "int16" => typeof(short),
                        "uint16" => typeof(ushort),
                        "int64" => typeof(long),
                        "uint64" => typeof(ulong),
                        "bool" => typeof(bool),
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
#endif
            }
        }
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void ValidateTransfer(int size, IReadOnlyList<ShaderTransferField> fields, string name)
    {
        if (Name != name || Size != size || fields.Count != _members.Count)
        {
            throw new ArgumentException($"C# transfer layout for {name} does not match shader {Name}: C# {size}, shader {Size} bytes.");
        }

        foreach (ShaderTransferField field in fields)
        {
            if (!_members.TryGetValue(field.Path, out ShaderMember? member) || member.Offset != field.Offset || member.Size != field.Size || member.Reference != field.IsReference || (!field.IsReference && member.ValueType != field.ValueType))
            {
                throw new ArgumentException($"C# transfer field {name}.{field.Path} does not match the shader layout.");
            }
        }
    }

    private sealed record ShaderMember(int Offset, int Size, bool Reference, string Kind, Type? ValueType, string? Target);
}
