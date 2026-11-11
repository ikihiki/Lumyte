using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Provides deterministic synthetic ABI and mapped-memory fixtures.</summary>
public sealed partial class PersistentStorageTests
{
    private static ShaderValue Leaf(ShaderValueSnapshot snapshot, string path) => Assert.Single(snapshot.Values, value => value.Path == path);

    private static float Number(ShaderValueSnapshot snapshot) => MemoryMarshal.Read<float>(Leaf(snapshot, "Number").Data.Span);

    private static void AssertComplete(ShaderValueSnapshot snapshot, float number, Ref buffer, Ref other, int stride)
    {
        Assert.Equal("arguments", snapshot.RootParameter);
        Assert.Equal("PersistentValue", snapshot.ShaderTypeName);
        Assert.Equal(5, snapshot.Values.Count);
        Assert.Equal(number, Number(snapshot));
        Assert.Same(buffer, Leaf(snapshot, "Buffer").Reference);
        Assert.Same(other, Leaf(snapshot, "Other").Reference);
        Assert.Equal(typeof(float), Leaf(snapshot, "Buffer").ValueType);
        Assert.True(Leaf(snapshot, "Buffer").IsReference);
        foreach (ShaderValue value in snapshot.Values.Where(value => !value.IsReference))
        {
            Assert.Null(value.Reference);
            Assert.True(MemoryMarshal.TryGetArray(value.Data, out ArraySegment<byte> segment));
            Assert.NotNull(segment.Array);
            Assert.InRange(segment.Array.Length, value.Data.Length, stride);
        }
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

    private static JsonObject Helper() => Structure(
        "GpuBufferRef",
        16,
        4,
        Field("Id", Scalar("uint32"), 0, 4),
        Field("Offset", Scalar("uint32"), 4, 4),
        Field("Count", Scalar("uint32"), 8, 4),
        Field("Reserved", Scalar("uint32"), 12, 4));

    private static ShaderDataLayout Layout(Action<JsonObject>? mutate = null, string referenceTarget = "float")
    {
        JsonObject nested = Structure("NestedValue", 16, 16, Field("Color", new JsonObject { ["kind"] = "vector", ["elementCount"] = 4, ["elementType"] = Scalar("float32") }, 0, 16));
        var matrix = new JsonObject
        {
            ["kind"] = "matrix",
            ["rowCount"] = 4,
            ["columnCount"] = 4,
            ["elementType"] = Scalar("float32"),
            ["matrixLayout"] = "row-major",
        };
        JsonObject type = Structure(
            "PersistentValue",
            128,
            16,
            Field("Number", Scalar("float32"), 0, 4),
            Field("Nested", nested, 16, 16),
            Field("Transform", matrix, 32, 64),
            Field("Buffer", Helper(), 96, 16),
            Field("Other", Helper(), 112, 16));
        mutate?.Invoke(type);
        var metadata = new JsonObject
        {
            ["lumyteAbi"] = 1,
            ["lumyteReferenceTargets"] = new JsonObject { ["PersistentValue.Buffer"] = referenceTarget, ["PersistentValue.Other"] = "float" },
            ["parameters"] = new JsonArray(new JsonObject
            {
                ["name"] = "__lumyte_schema_PersistentValue",
                ["type"] = new JsonObject
                {
                    ["kind"] = "resource",
                    ["resultType"] = new JsonObject { ["kind"] = "pointer", ["valueType"] = type },
                },
            }),
        };
        var artifact = ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", [1], metadata.ToJsonString());
        return ShaderDataLayout.Data(artifact, ShaderTarget.Wgsl, "PersistentValue");
    }

    private static void Corrupt(JsonObject type, string defect)
    {
        JsonNode fields = type["fields"]!;
        JsonNode helper = fields[3]!["type"]!;
        JsonNode matrix = fields[2]!["type"]!;
        switch (defect)
        {
            case "offset":
                fields[0]!["binding"]!["offset"] = -4;
                break;
            case "alignment":
                type["sizes"]![0]!["alignment"] = 3;
                break;
            case "stride":
                type["sizes"]![0]!["value"] = 127;
                break;
            case "scalar-size":
                fields[0]!["binding"]!["size"] = 8;
                break;
            case "matrix-element":
                matrix["elementType"]!["scalarType"] = "uint32";
                break;
            case "matrix-shape":
                matrix["columnCount"] = 3;
                break;
            case "matrix-layout":
                matrix["matrixLayout"] = "column-major";
                break;
            case "helper-kind":
                helper["kind"] = "vector";
                break;
            case "helper-size":
                fields[3]!["binding"]!["size"] = 12;
                break;
            case "helper-order":
                helper["fields"]![0]!["name"] = "Offset";
                helper["fields"]![1]!["name"] = "Id";
                break;
            case "helper-field-kind":
                helper["fields"]![0]!["type"]!["kind"] = "matrix";
                break;
            case "helper-field-type":
                helper["fields"]![0]!["type"]!["scalarType"] = "float32";
                break;
            case "helper-offset":
                helper["fields"]![1]!["binding"]!["offset"] = 0;
                break;
            case "target":
                helper["name"] = "GpuTextureRef";
                break;
            default: throw new ArgumentException("Unknown reflection defect.", nameof(defect));
        }
    }

    // Deliberately handwritten instrumentation with the same immutable truthful metadata as generated code.
    private readonly record struct Value(float Number, Vector4 Color, Matrix4x4 Transform, Ref Buffer, Ref Other) : IShaderData
    {
        public static int Captures { get; set; }

        public static string ShaderTypeName => "PersistentValue";

        public static IReadOnlyList<ShaderDataMember> StorageSchema { get; } = Array.AsReadOnly<ShaderDataMember>([
            new("Number", typeof(float), false),
            new("Nested.Color", typeof(Vector4), false),
            new("Transform", typeof(Matrix4x4), false),
            new("Buffer", typeof(float), true, "float"),
            new("Other", typeof(float), true, "float"),
        ]);

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            Captures++;
            writer.WriteValue("Number", Number);
            writer.WriteValue("Nested.Color", Color);
            writer.WriteValue("Transform", Transform);
            writer.WriteReference<float>("Buffer", Buffer);
            writer.WriteReference<float>("Other", Other);
        }
    }

    private readonly record struct Legacy(float Number) : IShaderData
    {
        public static string ShaderTypeName => "PersistentValue";

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => writer.WriteValue("Number", Number);
    }

    private sealed class Fixture
    {
        public object Owner { get; } = new();

        public ShaderDataLayout Layout { get; } = PersistentStorageTests.Layout();

        public int Encodes { get; private set; }

        public string? Failure { get; set; }

        public string FailPath { get; set; } = "Buffer";

        public Ref Reference(int id) => new(Owner, id);

        public Value Make(int number) => new(number, new Vector4(number, 2, 3, 4), Matrix4x4.Identity, Reference(number), Reference(number + 1));

        public int Encode(ShaderDataMember member, object value, string kind, Span<byte> destination)
        {
            Encodes++;
            if (value is not Ref reference || !ReferenceEquals(reference.Owner, Owner) || kind != "GpuBufferRef")
            {
                throw new ArgumentException("Invalid reference provenance or kind.", nameof(value));
            }

            if (member.Path == FailPath && Failure != null)
            {
                if (Failure == "short")
                {
                    return 15;
                }

                if (Failure == "null")
                {
                    throw new ArgumentNullException(nameof(value));
                }

                if (Failure == "foreign")
                {
                    throw new ArgumentException("Wrong owner.", nameof(value));
                }

                throw new InvalidOperationException("Injected encoder failure.");
            }

            destination.Clear();
            int id = reference.Id;
            MemoryMarshal.Write(destination, in id);
            return 16;
        }
    }

    private sealed class Ref(object owner, int id) : IGpuRef<float>
    {
        public object Owner { get; } = owner;

        public int Id { get; } = id;

        public ulong Count => 1;

        public IGpuRef<float> GetElement(ulong index) => index == 0 ? this : throw new ArgumentOutOfRangeException(nameof(index));
    }

    private sealed class ForeignRef<T> : IGpuRef<T>
    {
        public ulong Count => 1;

        public IGpuRef<T> GetElement(ulong index) => this;
    }

    private sealed class ForeignStorage : ShaderDataStorage<Value>
    {
        public override int Count => 0;

        public override TValue GetValue<TValue>(int index, ShaderDataValueMember<Value, TValue> member) => throw new NotSupportedException();

        public override IGpuRef<TReference> GetReference<TReference>(int index, ShaderDataReferenceMember<Value, TReference> member) => throw new NotSupportedException();

        public override void Set(int index, in Value value) => throw new NotSupportedException();

        public override void SetValue<TValue>(int index, ShaderDataValueMember<Value, TValue> member, in TValue value) => throw new NotSupportedException();

        public override void SetReference<TReference>(int index, ShaderDataReferenceMember<Value, TReference> member, IGpuRef<TReference> value) => throw new NotSupportedException();
    }

    private sealed class FakeBuffer(int size) : IGraphicsBuffer<byte>
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
