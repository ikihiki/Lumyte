using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Tests;

/// <summary>Provides isolated synthetic reflection and allocation-free reference encoding.</summary>
public sealed partial class PersistentPropertyTests
{
    /// <summary>Identifies a fixture registration without wire-array allocation.</summary>
    private interface IReference
    {
        /// <summary>Gets the fixture provenance owner.</summary>
        object Owner { get; }

        /// <summary>Gets the deterministic fake wire value.</summary>
        int Id { get; }
    }

    private static JsonObject Scalar(string type) => new() { ["kind"] = "scalar", ["scalarType"] = type };

    private static JsonObject Field(string name, JsonObject type, int offset, int size) => new()
    {
        ["name"] = name,
        ["type"] = type,
        ["binding"] = new JsonObject { ["offset"] = offset, ["size"] = size },
    };

    private static JsonObject Structure(string name, int size, int alignment, params JsonObject[] fields) => new()
    {
        ["kind"] = "struct",
        ["name"] = name,
        ["sizes"] = new JsonArray(new JsonObject { ["kind"] = "uniform", ["value"] = size, ["alignment"] = alignment }),
        ["fields"] = new JsonArray(fields.Cast<JsonNode>().ToArray()),
    };

    private static JsonObject Vector(int count) => new()
    {
        ["kind"] = "vector",
        ["elementCount"] = count,
        ["elementType"] = Scalar("float32"),
    };

    private static JsonObject Helper(string kind) => Structure(kind, 16, 4, Field("Id", Scalar("uint32"), 0, 4), Field("Offset", Scalar("uint32"), 4, 4), Field("Count", Scalar("uint32"), 8, 4), Field("Reserved", Scalar("uint32"), 12, 4));

    private static ShaderDataLayout Layout(bool matrix)
    {
        string name = matrix ? "BindingMatrixData" : "BindingMaterial";
        var transform = new JsonObject
        {
            ["kind"] = "matrix",
            ["rowCount"] = 4,
            ["columnCount"] = 4,
            ["elementType"] = Scalar("float32"),
            ["matrixLayout"] = "row-major",
        };
        JsonObject type = matrix
            ? Structure(name, 96, 16, Field("Transform", transform, 0, 64), Field("Child", Structure("BindingVector", 16, 16, Field("Value", Vector(3), 0, 12)), 64, 16), Field("Node", Helper("GpuBufferRef"), 80, 16))
            : Structure(name, 48, 16, Field("Color", Vector(4), 0, 16), Field("Texture", Helper("GpuTextureRef"), 16, 16), Field("Sampler", Helper("GpuSamplerRef"), 32, 16));
        var metadata = new JsonObject
        {
            ["lumyteAbi"] = 1,
            ["lumyteReferenceTargets"] = new JsonObject { ["BindingMatrixData.Node"] = "BindingNode" },
            ["parameters"] = new JsonArray(new JsonObject
            {
                ["name"] = "__lumyte_schema_" + name,
                ["type"] = new JsonObject
                {
                    ["kind"] = "resource",
                    ["resultType"] = new JsonObject { ["kind"] = "pointer", ["valueType"] = type },
                },
            }),
        };
        var artifact = ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", [1], metadata.ToJsonString());
        return ShaderDataLayout.Data(artifact, ShaderTarget.Wgsl, name);
    }

    private sealed class Reference<T>(object owner, int id) : IGpuRef<T>, IReference
    {
        public object Owner { get; } = owner;

        public int Id { get; } = id;

        public ulong Count => 1;

        public IGpuRef<T> GetElement(ulong index) => index == 0 ? this : throw new ArgumentOutOfRangeException(nameof(index));
    }

    private sealed class Fixture
    {
        public object Owner { get; } = new();

        public ShaderDataLayout MatrixLayout { get; } = Layout(true);

        public ShaderDataLayout MaterialLayout { get; } = Layout(false);

        public int Encodes { get; private set; }

        public bool ShortEncoding { get; set; }

        public Reference<T> Reference<T>(int id) => new(Owner, id);

        public int Encode(ShaderDataMember member, object value, string kind, Span<byte> destination)
        {
            Encodes++;
            if (value is not IReference reference || !ReferenceEquals(reference.Owner, Owner) || kind is not ("GpuBufferRef" or "GpuTextureRef" or "GpuSamplerRef"))
            {
                throw new ArgumentException("Reference provenance or kind mismatch.", nameof(value));
            }

            destination.Fill(0xCC);
            int id = reference.Id;
            MemoryMarshal.Write(destination, in id);
            return ShortEncoding ? 15 : 16;
        }
    }

    private sealed class MappedBuffer(int size) : IGraphicsBuffer<byte>
    {
        public byte[] Bytes { get; } = new byte[size];

        public bool FailWrites { get; set; }

        public int Writes { get; private set; }

        public ulong SizeInBytes => (ulong)Bytes.Length;

        public BufferLayout<byte> Layout => new(1, 1, 1, 1);

        public ulong Count => SizeInBytes;

        public BufferUsage Usage => BufferUsage.CopySource;

        public MemoryPreference Memory => MemoryPreference.Upload;

        public bool IsMapped => true;

        public void ValidateByteRange(ulong offset, ulong length)
        {
            if (length == 0 || offset > Count || length > Count - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
        }

        public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
        {
            ValidateByteRange(offset, length);
            if (FailWrites)
            {
                throw new InvalidOperationException("Injected mapped buffer failure.");
            }

            Writes++;
            source.CopyTo(Bytes.AsSpan(checked((int)offset), checked((int)length)));
        }

        public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => Bytes.AsSpan(checked((int)offset), checked((int)length)).CopyTo(destination);

        public ValueTask MapAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Unmap()
        {
        }

        public BufferSlice<byte> SliceElements(ulong offset, ulong count) => new(this, offset, count);

        public void CopyFrom(ReadOnlySpan<byte> source) => CopyBytesFrom(source, 0, (ulong)source.Length);

        public void CopyTo(Span<byte> destination) => Bytes.AsSpan().CopyTo(destination);

        public void Dispose()
        {
        }
    }
}
