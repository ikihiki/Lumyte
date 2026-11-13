using System.Buffers.Binary;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks reflected bulk numeric packing and handwritten codec fallback.</summary>
public sealed class ShaderBulkCaptureTests
{
    /// <summary>Checks exact uint bits, noncontiguous offsets and independent historical captures.</summary>
    [Fact]
    public void NumericBatchMatchesGenericAndOwnsSnapshotBytes()
    {
        ShaderDataLayout layout = Layout();
        var calls = new List<int>();
        Datum[] input = [new(0x7fc12345, 0x80000000, 0, calls), new(17, 29, 0, calls)];
        byte[] bytes = layout.Pack<Datum>(input, (_, _) => throw new InvalidOperationException(), out ShaderValueSnapshot[] snapshots);
        Assert.Equal(new[] { 0, 0 }, calls);
        for (int i = 0; i < input.Length; i++)
        {
            Assert.Equal(layout.Pack(IShaderArguments.Capture(in input[i]), (_, _) => throw new InvalidOperationException()), bytes.AsSpan(i * 16, 16).ToArray());
            Assert.Equal(input[i].First, BinaryPrimitives.ReadUInt32LittleEndian(snapshots[i].Values[0].Data.Span));
            Assert.Equal(input[i].Second, BinaryPrimitives.ReadUInt32LittleEndian(snapshots[i].Values[1].Data.Span));
        }

        Array.Clear(bytes);
        input[0] = new(1, 2, 0, calls);
        _ = layout.Pack<Datum>(input, (_, _) => [], out _);
        Assert.Equal(0x7fc12345u, BinaryPrimitives.ReadUInt32LittleEndian(snapshots[0].Values[0].Data.Span));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshots[0].Values[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshots[0].Values[2]);
    }

    /// <summary>Reordered codecs run once and preserve emission order through the fallback.</summary>
    [Fact]
    public void ReorderedCodecFallsBackWithoutReplay()
    {
        var calls = new List<int>();
        Datum[] input = [new(7, 13, 1, calls)];
        byte[] bytes = Layout().Pack<Datum>(input, (_, _) => [], out ShaderValueSnapshot[] snapshots);
        Assert.Single(calls);
        Assert.Equal(new[] { "Second", "First" }, snapshots[0].Values.Select(value => value.Path));
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
        Assert.Throws<ArgumentException>(() => Layout().Pack<Datum>(input, (_, _) => [], out _));
        Assert.Equal(2, calls.Count);
    }

    /// <summary>A throwing handwritten codec is not replayed or followed by later captures.</summary>
    [Fact]
    public void CodecExceptionStopsCaptureOnce()
    {
        var calls = new List<int>();
        Datum[] input = [new(7, 13, 5, calls), new(11, 19, 0, calls)];
        Assert.Throws<InvalidOperationException>(() => Layout().Pack<Datum>(input, (_, _) => [], out _));
        Assert.Single(calls);
    }

    /// <summary>Reference callbacks still run after every codec, once per emitted reference.</summary>
    [Fact]
    public void ReferenceFallbackPreservesCaptureAndEncodingOrder()
    {
        var events = new List<string>();
        RefDatum[] input = [new(7, events), new(9, events)];
        ShaderDataLayout layout = ReferenceLayout();
        byte[] bytes = layout.Pack<RefDatum>(
            input,
            (member, kind) =>
        {
            events.Add("encode");
            Assert.Equal("GpuTextureRef", kind);
            Assert.Null(member.Reference);
            return Enumerable.Repeat((byte)0xab, 16).ToArray();
        },
            out ShaderValueSnapshot[] snapshots);
        Assert.Equal(new[] { "write", "write", "encode", "encode" }, events);
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(9u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32)));
        Assert.All(snapshots, snapshot => Assert.Equal(4, snapshot.Values[0].Data.Length));
        Assert.All(snapshots, snapshot => Assert.Null(snapshot.Values[1].Reference));
        Assert.Throws<ArgumentException>(() => layout.ValidateArguments(snapshots[0], true));
    }

    /// <summary>Overlapping shader storage is rejected before codec execution.</summary>
    [Fact]
    public void OverlappingShaderLayoutIsRejectedBeforeCapture() => Assert.Throws<NotSupportedException>(() => Layout(secondOffset: 0));

    /// <summary>Negative reflected offsets are rejected as invalid schemas.</summary>
    [Fact]
    public void InvalidShaderOffsetIsRejectedBeforeCapture() => Assert.Throws<NotSupportedException>(() => Layout(firstOffset: -1));

    /// <summary>Names are diagnostic only: changed labels do not select GPU placement.</summary>
    [Fact]
    public void DiagnosticNamesCannotRedirectFixedByteOffsets()
    {
        var calls = new List<int>();
        Datum[] input = [new(7, 13, 6, calls)];
        byte[] bytes = Layout().Pack<Datum>(input, (_, _) => [], out ShaderValueSnapshot[] snapshots);
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(13u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(new[] { "not-a-shader-field", "arbitrary-diagnostic" }, snapshots[0].Values.Select(value => value.Path));
        Assert.Equal(new[] { 0, 8 }, snapshots[0].Values.Select(value => value.Offset));
    }

    /// <summary>The removed string-only writer and layout resolver APIs are absent.</summary>
    [Fact]
    public void PublicSerializationContractRequiresOffsets()
    {
        Assert.All(typeof(IShaderValueWriter).GetMethods(), method => Assert.Equal(typeof(int), method.GetParameters()[0].ParameterType));
        Assert.Equal(typeof(int), typeof(IShaderDataLayout).GetMethod(nameof(IShaderDataLayout.ReferenceKind))!.GetParameters()[0].ParameterType);
        Assert.All(typeof(ShaderValue).GetConstructors(), constructor => Assert.NotEqual(typeof(string), constructor.GetParameters()[0].ParameterType));
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

    private readonly record struct RefDatum(uint Value, List<string> Events) : IShaderData
    {
        public static string ShaderTypeName => "RefDatum";

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            Events.Add("write");
            writer.WriteValue(0, nameof(Value), Value);
            writer.WriteReference<IGraphicsTextureView>(16, "Texture", null);
        }
    }

    private readonly record struct Datum(uint First, uint Second, int Mode, List<int> Calls) : IShaderData
    {
        public static string ShaderTypeName => "Datum";

        public static int WireSizeInBytes => 16;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter
        {
            Calls.Add(Mode);
            if (Mode == 6)
            {
                writer.WriteValue(0, "not-a-shader-field", First);
                writer.WriteValue(8, "arbitrary-diagnostic", Second);
                return;
            }

            if (Mode == 5)
            {
                throw new InvalidOperationException("codec failure");
            }

            if (Mode == 1)
            {
                writer.WriteValue(8, nameof(Second), Second);
                writer.WriteValue(0, nameof(First), First);
                return;
            }

            writer.WriteValue(0, nameof(First), First);
            if (Mode == 2)
            {
                return;
            }

            if (Mode == 3)
            {
                writer.WriteValue(0, nameof(First), Second);
            }
            else if (Mode == 4)
            {
                writer.WriteValue(8, nameof(Second), unchecked((int)Second));
            }
            else
            {
                writer.WriteValue(8, nameof(Second), Second);
            }
        }
    }
}
