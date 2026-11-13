using System.Runtime.InteropServices;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks byte-authoritative packing and optional reflection diagnostics.</summary>
public sealed class ShaderBulkTransferTests
{
    /// <summary>Matching reflection does not reconstruct or mutate transferred bytes.</summary>
    [Fact]
    public void PackCopiesRawBytesWithoutCallingReferenceEncoder()
    {
        var layout = ShaderDataLayout.Root(Target(), "arguments");
        byte[] bytes = [0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10];
        var snapshot = new ShaderValueSnapshot("arguments", "Wire", bytes, [], Fields());
        byte[] packed = layout.Pack(snapshot, (_, _) => throw new InvalidOperationException());

        Assert.Equal(bytes, packed);
        Assert.NotSame(bytes, packed);
        packed[0] = 0;
        Assert.Equal(0xFE, snapshot.Data.Span[0]);
    }

    /// <summary>Defensive copies isolate raw bytes and metadata while keeping resource identity non-owning.</summary>
    [Fact]
    public void SnapshotCopyIsolatesRawBytesAndMetadata()
    {
        byte[] bytes = new byte[16];
        object reference = new();
        ShaderValue[] values = [new("Resource", typeof(IGraphicsTextureView), reference, 0)];
        ShaderTransferField[] fields = [new("Resource", 0, 16, typeof(ShaderReferenceSlot), true)];
        var snapshot = new ShaderValueSnapshot("arguments", "Wire", bytes, values, fields);
        ShaderValueSnapshot copy = snapshot.Copy();
        bytes[0] = 7;
        values[0] = values[0] with { Reference = null };
        fields[0] = fields[0] with { Offset = 8 };

        Assert.Equal(0, copy.Data.Span[0]);
        Assert.Same(reference, Assert.Single(copy.Values).Reference);
        Assert.Equal(0, Assert.Single(copy.Fields).Offset);
    }

    /// <summary>All numeric schema mismatches are diagnostics only in Release.</summary>
    /// <param name="mismatch">The mismatching schema component.</param>
    [Theory]
    [InlineData("name")]
    [InlineData("size")]
    [InlineData("count")]
    [InlineData("path")]
    [InlineData("offset")]
    [InlineData("fieldSize")]
    [InlineData("type")]
    [InlineData("reference")]
    public void NumericSchemaMismatchIsDebugOnly(string mismatch)
    {
        var layout = ShaderDataLayout.Root(Target(), "arguments");
        byte[] bytes = mismatch == "size" ? [1, 2, 3, 4] : [1, 2, 3, 4, 5, 6, 7, 8];
        ShaderTransferField[] fields = Fields();
        fields[0] = mismatch switch
        {
            "path" => fields[0] with { Path = "Other" },
            "offset" => fields[0] with { Offset = 4 },
            "fieldSize" => fields[0] with { Size = 8 },
            "type" => fields[0] with { ValueType = typeof(float) },
            "reference" => fields[0] with { IsReference = true },
            _ => fields[0],
        };
        var snapshot = new ShaderValueSnapshot("arguments", mismatch == "name" ? "Other" : "Wire", bytes, [], mismatch == "count" ? [] : fields);
#if DEBUG
        Assert.Throws<ArgumentException>(() => layout.ValidateArguments(snapshot, validateReferences: true));
        Assert.Throws<ArgumentException>(() => layout.Pack(snapshot, (_, _) => throw new InvalidOperationException()));
#else
        layout.ValidateArguments(snapshot, validateReferences: true);
        Assert.Equal(bytes, layout.Pack(snapshot, (_, _) => throw new InvalidOperationException()));
#endif
    }

    /// <summary>Reference replacement only changes its explicit sixteen-byte C# slot.</summary>
    [Fact]
    public void ReferenceFixupPreservesSurroundingNumericBytes()
    {
        var layout = ShaderDataLayout.Root(Target(reference: true), "arguments");
        byte[] bytes = Enumerable.Range(0, 24).Select(index => (byte)index).ToArray();
        object reference = new();
        var snapshot = new ShaderValueSnapshot("arguments", "Wire", bytes, [new("Resource", typeof(IGraphicsTextureView), reference, 4)], [new("First", 0, 4, typeof(uint)), new("Resource", 4, 16, typeof(ShaderReferenceSlot), true), new("Last", 20, 4, typeof(uint))]);
        int calls = 0;
        byte[] packed = layout.Pack(snapshot, (value, kind) =>
        {
            calls++;
            Assert.Same(reference, value.Reference);
            Assert.Equal("GpuTextureRef", kind);
            return Enumerable.Repeat((byte)0xA5, 16).ToArray();
        });

        Assert.Equal(1, calls);
        Assert.Equal(bytes[..4], packed[..4]);
        Assert.Equal(Enumerable.Repeat((byte)0xA5, 16).ToArray(), packed[4..20]);
        Assert.Equal(bytes[20..], packed[20..]);
        Assert.Equal(Enumerable.Range(0, 24).Select(index => (byte)index).ToArray(), snapshot.Data.ToArray());
    }

    /// <summary>Reference fixups use the C# offset even when Release shader metadata disagrees.</summary>
    [Fact]
    public void ReferenceOffsetMismatchIsDebugOnly()
    {
        var layout = ShaderDataLayout.Root(Target(reference: true), "arguments");
        byte[] bytes = Enumerable.Range(0, 24).Select(index => (byte)index).ToArray();
        var snapshot = new ShaderValueSnapshot("arguments", "Wire", bytes, [new("Resource", typeof(IGraphicsTextureView), new object(), 0)], [new("First", 20, 4, typeof(uint)), new("Resource", 0, 16, typeof(ShaderReferenceSlot), true), new("Last", 16, 4, typeof(uint))]);
#if DEBUG
        Assert.Throws<ArgumentException>(() => layout.Pack(snapshot, (_, _) => new byte[16]));
#else
        byte[] packed = layout.Pack(snapshot, (_, _) => new byte[16]);
        Assert.Equal(new byte[16], packed[..16]);
        Assert.Equal(bytes[16..], packed[16..]);
#endif
    }

    /// <summary>Static wire metadata determines data stride without creating a reference-containing instance.</summary>
    [Fact]
    public void DataLayoutUsesStaticTransferSizeWithoutConstructingValue()
    {
        using var document = JsonDocument.Parse(Target().ReflectionJson);
        JsonElement wire = document.RootElement.GetProperty("parameters")[0].GetProperty("type").GetProperty("elementType");
        string json = JsonSerializer.Serialize(new
        {
            lumyteAbi = 1,
            parameters = new[]
            {
                new
                {
                    name = "__lumyte_schema_Wire",
                    type = new { kind = "resource", resultType = new { kind = "pointer", valueType = wire } },
                },
            },
        });
        var artifact = ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", new byte[] { 32 }, json);
        var layout = ShaderDataLayout.Data<UnconstructibleData>(artifact, ShaderTarget.Wgsl);

        Assert.Equal("Wire", layout.Name);
        Assert.Equal(UnconstructibleData.TransferSize, layout.Size);
#if DEBUG
        Assert.Throws<ArgumentException>(() => ShaderDataLayout.Data<PaddedUnconstructibleData>(artifact, ShaderTarget.Wgsl));
#else
        Assert.Equal(12, ShaderDataLayout.Data<PaddedUnconstructibleData>(artifact, ShaderTarget.Wgsl).Size);
#endif
    }

    /// <summary>Malformed offsets never reach backend encoders, even in Release.</summary>
    /// <param name="offset">The invalid C# reference slot location.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(int.MaxValue)]
    public void OutOfBoundsReferenceSlotsAreAlwaysRejected(int offset)
    {
        var layout = ShaderDataLayout.Root(Target(reference: true), "arguments");
        var snapshot = new ShaderValueSnapshot("arguments", "Wire", new byte[24], [new("Resource", typeof(IGraphicsTextureView), new object(), offset)], [new("First", 0, 4, typeof(uint)), new("Resource", 4, 16, typeof(ShaderReferenceSlot), true), new("Last", 20, 4, typeof(uint))]);
        bool encoded = false;
        Assert.Throws<ArgumentException>(() => layout.Pack(snapshot, (_, _) =>
        {
            encoded = true;
            return new byte[16];
        }));
        Assert.False(encoded);
    }

    /// <summary>The common reference ABI cannot be shortened or enlarged in Release.</summary>
    /// <param name="length">The invalid encoded ABI size.</param>
    [Theory]
    [InlineData(15)]
    [InlineData(17)]
    public void ReferenceEncodingSizeIsAlwaysChecked(int length)
    {
        var layout = ShaderDataLayout.Root(Target(reference: true), "arguments");
        var snapshot = new ShaderValueSnapshot("arguments", "Wire", new byte[24], [new("Resource", typeof(IGraphicsTextureView), new object(), 4)], [new("First", 0, 4, typeof(uint)), new("Resource", 4, 16, typeof(ShaderReferenceSlot), true), new("Last", 20, 4, typeof(uint))]);
        Assert.Throws<ArgumentException>(() => layout.Pack(snapshot, (_, _) => new byte[length]));
    }

    /// <summary>Plain struct diagnostics use managed offsets, but Release never rearranges bytes.</summary>
    [Fact]
    public void OrdinaryStructLayoutMismatchIsDebugOnly()
    {
        var value = new OrdinaryWire { First = 17, Last = 29 };
        ShaderValueSnapshot snapshot = ShaderTransfer.Capture(in value, shaderTypeName: "Wire");
        var layout = ShaderDataLayout.Root(Target(), "arguments");
#if DEBUG
        Assert.Throws<ArgumentException>(() => layout.Pack(snapshot, (_, _) => throw new InvalidOperationException()));
#else
        Assert.Equal(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)).ToArray(), layout.Pack(snapshot, (_, _) => throw new InvalidOperationException()));
#endif
    }

    private static ShaderTransferField[] Fields() => [new("First", 0, 4, typeof(uint)), new("Last", 4, 4, typeof(uint))];

    private static ShaderTargetData Target(bool reference = false)
    {
        object scalar = new { kind = "scalar", scalarType = "uint32" };
        object[] fields = reference
            ? [new { name = "First", type = scalar, binding = new { offset = 0, size = 4 } },
               new { name = "Resource", type = (object)new { kind = "struct", name = "GpuTextureRef" }, binding = new { offset = 4, size = 16 } },
               new { name = "Last", type = scalar, binding = new { offset = 20, size = 4 } }]
            : [new { name = "First", type = scalar, binding = new { offset = 0, size = 4 } },
               new { name = "Last", type = scalar, binding = new { offset = 4, size = 4 } }];
        string json = JsonSerializer.Serialize(new
        {
            parameters = new[]
            {
                new
                {
                    name = "arguments",
                    type = new
                    {
                        kind = "constantBuffer",
                        elementType = new
                        {
                            kind = "struct",
                            name = "Wire",
                            sizes = new[] { new { kind = "uniform", value = reference ? 24 : 8, alignment = 4 } },
                            fields,
                        },
                    },
                },
            },
        });
        return new(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", "row-major", [32], json);
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    private struct OrdinaryWire
    {
        [FieldOffset(4)]
        public uint First;

        [FieldOffset(0)]
        public uint Last;
    }

    private sealed class PaddedUnconstructibleData : IShaderData
    {
        private PaddedUnconstructibleData()
        {
            throw new InvalidOperationException("Static sizing must not construct values.");
        }

        public static string ShaderTypeName => "Wire";

        public static int TransferSize => 12;

        public static IReadOnlyList<ShaderTransferField> TransferFields => Fields();

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => throw new InvalidOperationException("Static sizing must not capture values.");
    }

    private sealed class UnconstructibleData : IShaderData
    {
        private UnconstructibleData()
        {
            throw new InvalidOperationException("Must not construct shader values to determine stride.");
        }

        public static string ShaderTypeName => "Wire";

        public static int TransferSize => 8;

        public static IReadOnlyList<ShaderTransferField> TransferFields => Fields();

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter => throw new InvalidOperationException("Must not serialize shader values to determine stride.");
    }
}
