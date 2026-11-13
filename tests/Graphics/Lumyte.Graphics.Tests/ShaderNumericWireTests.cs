using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks explicit C# numeric wire layout authority without runtime codec repacking.</summary>
public sealed class ShaderNumericWireTests
{
    /// <summary>All supported numeric fields upload as one unchanged C# memory span.</summary>
    [Fact]
    public void ExplicitWireUploadsContiguouslyAndKeepsHistoricalSnapshots()
    {
        ShaderDataLayout layout = Layout();
        var value = new Wire(0x7fc12345u, -13, BitConverter.UInt32BitsToSingle(0xffc12345), new(1, 2), new(3, 4, 5), new(6, 7, 8, 9), Matrix4x4.CreateTranslation(10, 11, 12));
        Wire[] input = [value, value with { Unsigned = 0x80000000u }];
        layout.ValidateNumericWireData<Wire>();
        byte[] bytes = layout.Pack<Wire>(input, (_, _) => throw new InvalidOperationException(), out ShaderValueSnapshot[] snapshots);
        Assert.Equal(MemoryMarshal.AsBytes(input.AsSpan()).ToArray(), bytes);
        Assert.Equal(224, bytes.Length);
        Assert.Equal(0x7fc12345u, BinaryPrimitives.ReadUInt32LittleEndian(snapshots[0].Values[0].Data.Span));
        Assert.Equal(0x80000000u, BinaryPrimitives.ReadUInt32LittleEndian(snapshots[1].Values[0].Data.Span));
        Assert.Equal(0xffc12345u, BinaryPrimitives.ReadUInt32LittleEndian(snapshots[0].Values[2].Data.Span));
        Assert.Equal(7, snapshots[0].Values.Count);
        Array.Clear(bytes);
        input[0] = default;
        _ = layout.Pack<Wire>(input, (_, _) => [], out _);
        Assert.Equal(0x7fc12345u, BinaryPrimitives.ReadUInt32LittleEndian(snapshots[0].Values[0].Data.Span));
        Assert.Empty(ShaderBindingSnapshot.Capture(snapshots[0], null).References);
    }

    /// <summary>Mismatched shader strides or offsets fail admission rather than rearranging C# fields.</summary>
    /// <param name="stride">The incompatible shader stride.</param>
    /// <param name="offset">The incompatible shader first-field offset.</param>
    [Theory]
    [InlineData(128, 0)]
    [InlineData(112, 4)]
    public void IncompatibleShaderFailsBeforeUpload(int stride, int offset)
    {
        Assert.Throws<NotSupportedException>(() => Layout(stride, offset).ValidateNumericWireData<Wire>());
        Assert.Throws<NotSupportedException>(() => Layout(stride, offset).Pack<Wire>(new Wire[1], (_, _) => [], out _));
    }

    /// <summary>Opaque interface-bearing logical values cannot opt into numeric copying.</summary>
    [Fact]
    public void ManagedLogicalReferenceCannotBeBlitted() => Assert.Throws<NotSupportedException>(() => Layout().ValidateNumericWireData<ManagedWire>());

    /// <summary>A handwritten manifest cannot relocate the actual C# field or omit extra storage.</summary>
    [Fact]
    public void SpoofedNumericManifestIsRejectedAgainstActualStorage()
    {
        const string Reflection = """
            {"lumyteAbi":1,"parameters":[{"name":"__lumyte_schema_Spoof","type":{"kind":"resource","resultType":{"kind":"pointer","valueType":{"kind":"struct","name":"Spoof","sizes":[{"kind":"uniform","value":8,"alignment":4}],"fields":[{"name":"Value","type":{"kind":"scalar","scalarType":"uint32"},"binding":{"offset":0,"size":4}}]}}}}]}
            """;
        var artifact = ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", [1], Reflection);
        var layout = ShaderDataLayout.Data(artifact, ShaderTarget.Wgsl, "Spoof");
        Assert.Throws<NotSupportedException>(() => layout.ValidateNumericWireData<Spoof>());
        Assert.Throws<NotSupportedException>(() => layout.ValidateNumericWireData<OmittedStorage>());
    }

    /// <summary>Backend creation and direct uploads never invoke the explicit numeric type's throwing codec.</summary>
    /// <returns>The completion of native upload mapping and immutable metadata checks.</returns>
    [GpuFact]
    public async Task VulkanNumericWireConstructionDoesNotCaptureCodecAsync()
    {
        using var device = VulkanDevice.Create();
        var value = new Wire(17, -2, 3, Vector2.One, Vector3.One, Vector4.One, Matrix4x4.Identity);
        using IGraphicsShaderDataBuffer<Wire> upload = ((IGraphicDevice)device).CreateShaderDataBuffer<Wire>(Artifact(ShaderTarget.SpirV), 2, MemoryPreference.Upload);
        await upload.MapAsync();
        upload.CopyFrom([value, value with { Unsigned = 29 }]);
        ShaderValueSnapshot first = ((IShaderDataSource)upload).Read(0);
        upload.CopyFrom([value with { Unsigned = 41 }]);
        upload.Unmap();
        Assert.Equal(17u, BinaryPrimitives.ReadUInt32LittleEndian(first.Values[0].Data.Span));
        Assert.Equal(41u, BinaryPrimitives.ReadUInt32LittleEndian(((IShaderDataSource)upload).Read(0).Values[0].Data.Span));
    }

    private static ShaderDataLayout Layout(int stride = 112, int firstOffset = 0)
        => ShaderDataLayout.Data(Artifact(ShaderTarget.Wgsl, stride, firstOffset), ShaderTarget.Wgsl, "Wire");

    private static ShaderArtifact Artifact(ShaderTarget target, int stride = 112, int firstOffset = 0)
    {
        object Scalar(string scalar) => new { kind = "scalar", scalarType = scalar };
        object Vector(int count) => new { kind = "vector", elementType = new { scalarType = "float32" }, elementCount = count };
        object Field(string name, object type, int offset, int size) => new { name, type, binding = new { offset, size } };
        var type = new
        {
            kind = "struct",
            name = "Wire",
            sizes = new[] { new { kind = "uniform", value = stride, alignment = 4 } },
            fields = new[]
            {
                Field("Unsigned", Scalar("uint32"), firstOffset, 4),
                Field("Signed", Scalar("int32"), 4, 4),
                Field("Scalar", Scalar("float32"), 8, 4),
                Field("Pair", Vector(2), 12, 8),
                Field("Triple", Vector(3), 20, 12),
                Field("Quad", Vector(4), 32, 16),
                Field("Matrix", new { kind = "matrix", rowCount = 4, columnCount = 4, elementType = new { scalarType = "float32" } }, 48, 64),
            },
        };
        string json = JsonSerializer.Serialize(new
        {
            lumyteAbi = 1,
            parameters = new[] { new { name = "__lumyte_schema_Wire", type = new { kind = "resource", resultType = new { kind = "pointer", valueType = type } } } },
        });
        byte[] code = target == ShaderTarget.SpirV ? new byte[20] : [1];
        if (target == ShaderTarget.SpirV)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(code, 0x07230203);
        }

        return ShaderArtifact.PackTarget(target, ShaderStage.Compute, "main", "test", code, json);
    }

    [StructLayout(LayoutKind.Explicit, Size = 112)]
    private readonly record struct Wire(
        [field: FieldOffset(0)] uint Unsigned,
        [field: FieldOffset(4)] int Signed,
        [field: FieldOffset(8)] float Scalar,
        [field: FieldOffset(12)] Vector2 Pair,
        [field: FieldOffset(20)] Vector3 Triple,
        [field: FieldOffset(32)] Vector4 Quad,
        [field: FieldOffset(48)] Matrix4x4 Matrix) : IShaderNumericWireData
    {
        private static readonly ShaderNumericWireLayout _wire = new(
        112,
        new(nameof(Unsigned), typeof(uint), 0, 4),
        new(nameof(Signed), typeof(int), 4, 4),
        new(nameof(Scalar), typeof(float), 8, 4),
        new(nameof(Pair), typeof(Vector2), 12, 8),
        new(nameof(Triple), typeof(Vector3), 20, 12),
        new(nameof(Quad), typeof(Vector4), 32, 16),
        new(nameof(Matrix), typeof(Matrix4x4), 48, 64));

        public static string ShaderTypeName => "Wire";

        public ShaderNumericWireLayout NumericWireLayout => _wire;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
            => throw new InvalidOperationException("Direct C# wire must not invoke the legacy codec.");
    }

    private readonly record struct ManagedWire(IGpuRef<IGraphicsTextureView>? Texture) : IShaderNumericWireData
    {
        public static string ShaderTypeName => "Wire";

        public ShaderNumericWireLayout NumericWireLayout => new(112);

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
            => writer.WriteReference(0, nameof(Texture), Texture);
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    private readonly record struct Spoof([field: FieldOffset(4)] uint Value) : IShaderNumericWireData
    {
        public static string ShaderTypeName => "Spoof";

        public ShaderNumericWireLayout NumericWireLayout => new(8, new ShaderNumericWireMember(nameof(Value), typeof(uint), 0, 4));

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
            => writer.WriteValue(4, nameof(Value), Value);
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    private readonly record struct OmittedStorage(
        [field: FieldOffset(0)] uint Value,
        [field: FieldOffset(0)] uint Overlap) : IShaderNumericWireData
    {
        public static string ShaderTypeName => "Spoof";

        public ShaderNumericWireLayout NumericWireLayout => new(8, new ShaderNumericWireMember(nameof(Value), typeof(uint), 0, 4));

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
            => writer.WriteValue(0, nameof(Value), Value);
    }
}
