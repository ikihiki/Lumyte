using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Certifies persistent transfer layouts independently of application values.</summary>
public sealed partial class ShaderDataLayout
{
    private readonly JsonElement _storageType;

    /// <summary>Certifies generated metadata and creates independently owned CPU transfer storage.</summary>
    /// <typeparam name="T">The generated logical shader data type.</typeparam>
    /// <param name="owner">The backend-private manufacture identity, never the publicly accessible device object.</param>
    /// <param name="initial">Initial logical values, which may be empty.</param>
    /// <param name="encoder">The backend encoder, including provenance, device and usage checks.</param>
    /// <returns>The device-specific persistent storage; never exposes its wire bytes.</returns>
    public ShaderDataStorage<T> CreateStorage<T>(object owner, ReadOnlySpan<T> initial, ShaderDataReferenceEncoder encoder)
        where T : struct, IShaderData
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(encoder);
        if (!BitConverter.IsLittleEndian)
        {
            throw new NotSupportedException("Persistent shader data requires a little-endian host.");
        }

        ShaderDataMember[] schema = T.StorageSchema.ToArray();
        Certify(schema, T.ShaderTypeName);
        return new PersistentStorage<T>(this, owner, schema, initial, encoder);
    }

    /// <summary>Freezes per-element metadata before directly copying retained bytes into mapped storage.</summary>
    /// <typeparam name="T">The logical data type.</typeparam>
    /// <param name="source">The factory-created persistent source.</param>
    /// <param name="owner">The destination backend-private manufacture identity.</param>
    /// <param name="destination">The mapped upload allocation, validated by the calling backend.</param>
    /// <param name="sourceOffset">The first source element.</param>
    /// <param name="count">The nonnegative copy count.</param>
    /// <param name="byteOffset">The destination byte offset.</param>
    /// <returns>Complete immutable snapshots, ready for allocation-free metadata publication.</returns>
    public ShaderValueSnapshot[] Upload<T>(ShaderDataStorage<T> source, object owner, IGraphicsBuffer<byte> destination, int sourceOffset, int count, ulong byteOffset)
        where T : struct, IShaderData
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (source is not PersistentStorage<T> storage || !ReferenceEquals(storage.Owner, owner) || (!ReferenceEquals(this, storage.Layout) && (!Matches(storage.Layout) || !JsonElement.DeepEquals(_storageType, storage.Layout._storageType))))
        {
            throw new ArgumentException("Persistent shader storage belongs to a different device or schema.", nameof(source));
        }

        if (sourceOffset < 0 || count < 0 || sourceOffset > storage.Count || count > storage.Count - sourceOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceOffset));
        }

        int byteCount = checked(count * Size);
        if (byteOffset > destination.Count || (ulong)byteCount > destination.Count - byteOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }

        var snapshots = new ShaderValueSnapshot[count];
        for (int i = 0; i < count; i++)
        {
            snapshots[i] = storage.Freeze(sourceOffset + i);
        }

        if (count != 0)
        {
            destination.SliceElements(byteOffset, (ulong)byteCount).CopyFrom(storage.Bytes.AsSpan(checked(sourceOffset * Size), byteCount));
        }

        return snapshots;
    }

    private static int NumericSize(Type type) => type == typeof(int) || type == typeof(uint) || type == typeof(float) ? 4
        : type == typeof(Vector2) ? 8 : type == typeof(Vector3) ? 12 : type == typeof(Vector4) ? 16
        : type == typeof(Matrix4x4) ? 64 : throw new NotSupportedException("Unsupported persistent numeric member type.");

    private static void CertifyStructure(JsonElement type)
    {
        if (type.GetProperty("kind").GetString() != "struct")
        {
            throw new NotSupportedException("Persistent storage requires a structure layout.");
        }

        JsonElement layout = type.GetProperty("sizes").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "uniform");
        int size = layout.GetProperty("value").GetInt32();
        int alignment = layout.GetProperty("alignment").GetInt32();
        if (size <= 0 || alignment < 4 || alignment > 16 || (alignment & (alignment - 1)) != 0 || size % alignment != 0)
        {
            throw new NotSupportedException("Unsupported persistent structure stride or alignment.");
        }

        int end = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement field in type.GetProperty("fields").EnumerateArray())
        {
            string name = field.GetProperty("name").GetString()!;
            JsonElement binding = field.GetProperty("binding");
            int offset = binding.GetProperty("offset").GetInt32();
            int fieldSize = binding.GetProperty("size").GetInt32();
            if (!names.Add(name) || offset < end || offset % 4 != 0 || fieldSize <= 0 || offset > size - fieldSize)
            {
                throw new NotSupportedException("Invalid persistent field offset, size, overlap or name.");
            }

            end = checked(offset + fieldSize);
            JsonElement member = field.GetProperty("type");
            string kind = member.GetProperty("kind").GetString()!;
            string? memberName = member.TryGetProperty("name", out JsonElement named) ? named.GetString() : null;
            int memberAlignment = 4;
            if (member.TryGetProperty("sizes", out JsonElement memberSizes))
            {
                JsonElement memberLayout = memberSizes.EnumerateArray().SingleOrDefault(value => value.GetProperty("kind").GetString() == "uniform");
                if (memberLayout.ValueKind != JsonValueKind.Undefined)
                {
                    memberAlignment = memberLayout.GetProperty("alignment").GetInt32();
                }
            }

            if (memberAlignment < 4 || memberAlignment > 16 || (memberAlignment & (memberAlignment - 1)) != 0 || offset % memberAlignment != 0 || alignment % memberAlignment != 0)
            {
                throw new NotSupportedException("Persistent member and containing structure alignment are incompatible.");
            }

            if (memberName is "GpuTextureRef" or "GpuSamplerRef" or "GpuBufferRef" or "GpuRWBufferRef")
            {
                CertifyHelper(member, fieldSize);
            }
            else if (kind == "struct")
            {
                CertifyStructure(member);
                int nestedSize = member.GetProperty("sizes").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "uniform").GetProperty("value").GetInt32();
                if (fieldSize != nestedSize)
                {
                    throw new NotSupportedException("Nested persistent field size differs from its structure stride.");
                }
            }
            else if (kind == "matrix")
            {
                if (member.GetProperty("rowCount").GetInt32() != 4 || member.GetProperty("columnCount").GetInt32() != 4 ||
                    member.GetProperty("elementType").GetProperty("scalarType").GetString() != "float32" || fieldSize != 64 ||
                    (member.TryGetProperty("matrixLayout", out JsonElement matrixLayout) && matrixLayout.GetString() != "row-major"))
                {
                    throw new NotSupportedException("Persistent matrices require contiguous row-major float4x4.");
                }
            }
            else if (kind == "scalar")
            {
                if (fieldSize != 4 || member.GetProperty("scalarType").GetString() is not ("int32" or "uint32" or "float32"))
                {
                    throw new NotSupportedException("Unsupported persistent scalar layout.");
                }
            }
            else if (kind == "vector")
            {
                int count = member.GetProperty("elementCount").GetInt32();
                if (count is < 2 or > 4 || fieldSize != count * 4 || member.GetProperty("elementType").GetProperty("scalarType").GetString() != "float32")
                {
                    throw new NotSupportedException("Unsupported persistent vector layout.");
                }
            }
            else
            {
                throw new NotSupportedException("Unsupported persistent member layout.");
            }
        }
    }

    private static void CertifyHelper(JsonElement type, int fieldSize)
    {
        CertifyStructure(type);
        JsonElement fields = type.GetProperty("fields");
        JsonElement size = type.GetProperty("sizes").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "uniform");
        if (fieldSize != 16 || size.GetProperty("value").GetInt32() != 16 || size.GetProperty("alignment").GetInt32() != 4 || fields.GetArrayLength() != 4)
        {
            throw new NotSupportedException("Persistent GPU reference requires the 16-byte helper ABI.");
        }

        string[] names = ["Id", "Offset", "Count", "Reserved"];
        for (int i = 0; i < names.Length; i++)
        {
            JsonElement field = fields[i];
            JsonElement scalar = field.GetProperty("type");
            JsonElement binding = field.GetProperty("binding");
            if (field.GetProperty("name").GetString() != names[i] || scalar.GetProperty("kind").GetString() != "scalar" || scalar.GetProperty("scalarType").GetString() != "uint32" || binding.GetProperty("offset").GetInt32() != i * 4 || binding.GetProperty("size").GetInt32() != 4)
            {
                throw new NotSupportedException("Persistent GPU reference helper fields differ from the backend ABI.");
            }
        }
    }

    private void Certify(ShaderDataMember[] schema, string name)
    {
        if (name != Name || schema.Length != _members.Count || schema.Select(m => m.Path).Distinct(StringComparer.Ordinal).Count() != schema.Length)
        {
            throw new ArgumentException("Generated and compiled shader member sets differ.");
        }

        CertifyStructure(_storageType);
        foreach (ShaderDataMember leaf in schema)
        {
            if (!_members.TryGetValue(leaf.Path, out ShaderMember? member) || member.Reference != leaf.IsReference || member.Offset < 0 || member.Size <= 0 || member.Offset > Size - member.Size)
            {
                throw new ArgumentException("Generated and compiled shader members differ: " + leaf.Path);
            }

            if (!leaf.IsReference)
            {
                if (member.ValueType != leaf.ValueType || member.Size != NumericSize(leaf.ValueType))
                {
                    throw new ArgumentException("Persistent numeric type or size mismatch: " + leaf.Path);
                }
            }
            else if (member.Size != 16 || (member.Kind == "GpuTextureRef" && leaf.ValueType != typeof(IGraphicsTextureView)) ||
                (member.Kind == "GpuSamplerRef" && leaf.ValueType != typeof(IGraphicsSampler)) ||
                (member.Kind is "GpuBufferRef" or "GpuRWBufferRef" && (leaf.ReferenceTarget == null || leaf.ReferenceTarget != member.Target)))
            {
                throw new ArgumentException("Persistent reference target mismatch: " + leaf.Path);
            }
        }
    }

    // Only backend encoders create this unmanaged value; identity always remains in the CPU sidecar.
    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private readonly record struct NativeReference(ulong First, ulong Second)
    {
        internal static NativeReference Encode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 16)
            {
                throw new ArgumentException("A backend reference must occupy exactly 16 bytes.");
            }

            return MemoryMarshal.Read<NativeReference>(bytes);
        }

        internal void Write(Span<byte> destination) => MemoryMarshal.Write(destination, in this);
    }
}
