using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks C#-authoritative bulk captures and handwritten codecs.</summary>
public sealed class ShaderBulkCaptureTests
{
    /// <summary>Checks exact uint bits, noncontiguous offsets and independent historical captures.</summary>
    [Fact]
    public void NumericBatchMatchesGenericAndOwnsSnapshotBytes()
    {
        ShaderDataLayout layout = Layout();
        var calls = new List<int>();
        Datum[] input = [new(0x7fc12345, 0x80000000, 0, calls), new(17, 29, 0, calls)];
        byte[] bytes = CaptureBatch(input, out ShaderValueSnapshot[] snapshots);
        Assert.Equal(new[] { 0, 0 }, calls);
        Assert.All(snapshots, snapshot => layout.ValidateArguments(snapshot, true));
        for (int i = 0; i < input.Length; i++)
        {
            Assert.Equal(IShaderArguments.Capture(in input[i]).Data.ToArray(), bytes.AsSpan(i * 16, 16).ToArray());
            Assert.Equal(input[i].First, BinaryPrimitives.ReadUInt32LittleEndian(snapshots[i].Data.Span));
            Assert.Equal(input[i].Second, BinaryPrimitives.ReadUInt32LittleEndian(snapshots[i].Data.Span[8..]));
        }

        Array.Clear(bytes);
        input[0] = new(1, 2, 0, calls);
        _ = CaptureBatch(input, out _);
        Assert.Equal(0x7fc12345u, BinaryPrimitives.ReadUInt32LittleEndian(snapshots[0].Data.Span));
        Assert.Empty(snapshots[0].Values);
        Assert.Equal(16, snapshots[0].Data.Length);
    }

    /// <summary>Reordered application construction runs once and preserves physical wire order.</summary>
    [Fact]
    public void ReorderedCodecFallsBackWithoutReplay()
    {
        var calls = new List<int>();
        Datum[] input = [new(7, 13, 1, calls)];
        byte[] bytes = CaptureBatch(input, out ShaderValueSnapshot[] snapshots);
        Assert.Single(calls);
        Assert.Empty(snapshots[0].Values);
        Assert.Equal(new[] { "First", "Second" }, snapshots[0].GetDiagnosticFields().Select(value => value.Path));
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(13u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
    }

    /// <summary>Missing, duplicate and same-sized wrong-type emissions remain invalid.</summary>
    /// <param name="mode">The malformed emission variant.</param>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MalformedCodecIsRejectedAfterCapture(int mode)
    {
        var calls = new List<int>();
        Datum[] input = [new(7, 13, mode, calls), new(11, 19, 0, calls)];
        if (mode == 2)
        {
            Assert.Throws<InvalidOperationException>(() => CaptureBatch(input, out _));
            Assert.Single(calls);
        }
        else
        {
            ShaderValueSnapshot[] snapshots = input.Select(item => IShaderArguments.Capture(in item)).ToArray();
            ShaderTransferField[] fields = Datum.TransferFields.ToArray();
            fields[1] = mode == 3 ? fields[1] with { Offset = 0 } : fields[1] with { ValueType = typeof(int) };
            Assert.Throws<ArgumentException>(() => Layout().ValidateArguments(snapshots[0] with { DiagnosticFields = null, Fields = fields }, true));
            Assert.Equal(2, calls.Count);
        }
    }

    /// <summary>A throwing handwritten codec is not replayed or followed by later captures.</summary>
    [Fact]
    public void CodecExceptionStopsCaptureOnce()
    {
        var calls = new List<int>();
        Datum[] input = [new(7, 13, 5, calls), new(11, 19, 0, calls)];
        Assert.Throws<InvalidOperationException>(() => CaptureBatch(input, out _));
        Assert.Single(calls);
    }

    /// <summary>Reference callbacks still run after every codec, once per emitted reference.</summary>
    [Fact]
    public void ReferenceFallbackPreservesCaptureAndEncodingOrder()
    {
        var events = new List<string>();
        RefDatum[] input = [new(7, events), new(9, events)];
        ShaderDataLayout layout = ReferenceLayout();
        byte[] bytes = CaptureBatch(input, out ShaderValueSnapshot[] snapshots);
        foreach (ShaderValueSnapshot snapshot in snapshots)
        {
            ShaderValue reference = Assert.Single(snapshot.Values);
            Assert.Equal(16, reference.Offset);
            Assert.Equal(typeof(IGraphicsTextureView), reference.ValueType);
            Assert.Null(reference.Reference);
            events.Add("encode");
            bytes.AsSpan((Array.IndexOf(snapshots, snapshot) * 32) + reference.Offset, 16).Fill(0xab);
        }

        Assert.Equal(new[] { "write", "write", "encode", "encode" }, events);
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(9u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32)));
        Assert.All(snapshots, snapshot => Assert.Equal(32, snapshot.Data.Length));
        Assert.All(snapshots, snapshot => Assert.Null(Assert.Single(snapshot.Values).Reference));
        Assert.All(bytes.Skip(16).Take(16), item => Assert.Equal((byte)0xab, item));
        Assert.All(bytes.Skip(48).Take(16), item => Assert.Equal((byte)0xab, item));
        Assert.Throws<ArgumentException>(() => layout.ValidateArguments(snapshots[0], true));
    }

    /// <summary>Overlapping shader storage is rejected before codec execution.</summary>
    [Fact]
    public void OverlappingShaderLayoutIsRejectedBeforeCapture() => Assert.Throws<ArgumentException>(() => Layout(secondOffset: 0).ValidateTransferLayout(16, Datum.TransferFields, "Datum"));

    /// <summary>Negative reflected offsets are rejected as invalid schemas.</summary>
    [Fact]
    public void InvalidShaderOffsetIsRejectedBeforeCapture() => Assert.Throws<ArgumentException>(() => Layout(firstOffset: -1).ValidateTransferLayout(16, Datum.TransferFields, "Datum"));

    /// <summary>Names are diagnostic only: changed labels do not select GPU placement.</summary>
    [Fact]
    public void DiagnosticNamesCannotRedirectFixedByteOffsets()
    {
        var calls = new List<int>();
        Datum[] input = [new(7, 13, 6, calls)];
        byte[] bytes = CaptureBatch(input, out ShaderValueSnapshot[] snapshots);
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(13u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
        ShaderTransferField[] renamed = Datum.TransferFields.Select((field, index) => field with { Path = index == 0 ? "not-a-shader-field" : "arbitrary-diagnostic" }).ToArray();
        ShaderValueSnapshot diagnostic = snapshots[0] with { DiagnosticFields = () => renamed, Fields = renamed };
        Assert.Equal(new[] { "not-a-shader-field", "arbitrary-diagnostic" }, diagnostic.GetDiagnosticFields().Select(field => field.Path));
        Assert.Equal(new[] { 0, 8 }, diagnostic.GetDiagnosticFields().Select(field => field.Offset));
        Assert.Equal(snapshots[0].Data.ToArray(), diagnostic.Data.ToArray());
        Assert.Throws<ArgumentException>(() => Layout().ValidateArguments(diagnostic, true));
    }

    /// <summary>The removed string-only writer and layout resolver APIs are absent.</summary>
    [Fact]
    public void PublicSerializationContractRequiresOffsets()
    {
        Assert.All(typeof(IShaderValueWriter).GetMethods(), method => Assert.Single(method.GetParameters()));
        Assert.Equal(typeof(ReadOnlySpan<byte>), typeof(IShaderValueWriter).GetMethod(nameof(IShaderValueWriter.WriteValue))!.GetParameters()[0].ParameterType);
        Assert.Null(typeof(IShaderDataLayout).GetMethod("ReferenceKind"));
        Assert.All(typeof(ShaderValue).GetConstructors().Where(constructor => constructor.IsPublic), constructor => Assert.NotEqual(typeof(string), constructor.GetParameters()[0].ParameterType));
    }

    private static ShaderDataLayout ReferenceLayout()
    {
        string reflection = """
            {"lumyteAbi":1,"parameters":[{"name":"__lumyte_schema_RefDatum","type":{"kind":"resource","resultType":{"kind":"pointer","valueType":{"kind":"struct","name":"RefDatum","sizes":[{"kind":"uniform","value":32,"alignment":16}],"fields":[{"name":"Value","type":{"kind":"scalar","scalarType":"uint32"},"binding":{"offset":0,"size":4}},{"name":"Texture","type":{"kind":"struct","name":"GpuTextureRef"},"binding":{"offset":16,"size":16}}]}}}}]}
            """;
        return ShaderDataLayout.Data(ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", [1], reflection).GetTarget(ShaderTarget.Wgsl), "RefDatum");
    }

    private static ShaderDataLayout Layout(int firstOffset = 0, int secondOffset = 8)
    {
        var type = new
        {
            kind = "struct",
            name = "Datum",
            sizes = new[] { new { kind = "uniform", value = 16, alignment = 4 } },
            fields = new[]
            {
                new { name = "First", type = new { kind = "scalar", scalarType = "uint32" }, binding = new { offset = firstOffset, size = 4 } },
                new { name = "Second", type = new { kind = "scalar", scalarType = "uint32" }, binding = new { offset = secondOffset, size = 4 } },
            },
        };
        string reflection = JsonSerializer.Serialize(new
        {
            lumyteAbi = 1,
            parameters = new[] { new { name = "__lumyte_schema_Datum", type = new { kind = "resource", resultType = new { kind = "pointer", valueType = type } } } },
        });
        return ShaderDataLayout.Data(ShaderArtifact.PackTarget(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", [1], reflection).GetTarget(ShaderTarget.Wgsl), "Datum");
    }

    private static byte[] CaptureBatch<T>(T[] input, out ShaderValueSnapshot[] snapshots)
        where T : struct, IShaderArguments
    {
        snapshots = input.Select(item => IShaderArguments.Capture(in item)).ToArray();
        return snapshots.SelectMany(snapshot => snapshot.Data.ToArray()).ToArray();
    }

    private readonly record struct RefDatum(uint Value, List<string> Events) : IShaderArguments
    {
        public static string ShaderTypeName => "RefDatum";

        public static int TransferSize => 32;

        public static int ReferenceCount => 1;

        public static IReadOnlyList<ShaderTransferField> TransferFields =>
            [new(nameof(Value), 0, 4, typeof(uint)), new("Texture", 16, 16, typeof(ShaderReferenceSlot), true)];

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            Events.Add("write");
            uint value = Value;
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
            writer.WriteValue(new byte[12]);
            writer.WriteReference<IGraphicsTextureView>(null);
        }
    }

    private readonly record struct Datum(uint First, uint Second, int Mode, List<int> Calls) : IShaderArguments
    {
        public static string ShaderTypeName => "Datum";

        public static int TransferSize => 16;

        public static int ReferenceCount => 0;

        public static IReadOnlyList<ShaderTransferField> TransferFields =>
            [new(nameof(First), 0, 4, typeof(uint)), new(nameof(Second), 8, 4, typeof(uint))];

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            Calls.Add(Mode);
            if (Mode == 5)
            {
                throw new InvalidOperationException("codec failure");
            }

            byte[] bytes = new byte[16];

            // Application construction order does not change the authoritative physical wire order.
            if (Mode == 1)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), Second);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, First);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, First);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), Second);
            }

            writer.WriteValue(Mode == 2 ? bytes.AsSpan(0, 4) : bytes);
        }
    }
}
