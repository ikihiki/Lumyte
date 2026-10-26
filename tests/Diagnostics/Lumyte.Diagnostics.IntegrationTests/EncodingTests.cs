using System.Buffers;
using System.Text.Json;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.MagicOnion;
using MessagePack;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks direct encoders against the existing protocol and detached output lifetime.</summary>
public sealed class EncodingTests
{
    // Captured from the generated WireMessage DTO formatter at revision 0c7a1c1, before its removal.
    private static readonly string[] _versionOnePublications =
    [
        "96D92431313131313131312D313131312D313131312D313131312D313131313131313131313131D92432323232323232322D323232322D323232322D323232322D32323232323232323232323201C0C09199A36C6F67CF7FFFFFFFFFFFFFFFA8656E636F64696E679501C2CF0020000000000001CB0000000000000000C0A57472616365A47370616EC0D3800000000000000084A4666C61679500C300CB0000000000000000C0A7696E74656765729501C2D38000000000000000CB0000000000000000C0A66E756D6265729502C200CBBFF4000000000000C0A4746578749503C200CB0000000000000000B0E697A5E69CACE8AA9E0A225CF09F9982",
        "96D92431313131313131312D313131312D313131312D313131312D313131313131313131313131D92432323232323232322D323232322D323232322D323232322D32323232323232323232323200D92433333333333333332D333333332D333333332D333333332D33333333333333333333333395A77375636365737384A4666C61679500C300CB0000000000000000C0A7696E74656765729501C2D38000000000000000CB0000000000000000C0A66E756D6265729502C200CBBFF4000000000000C0A4746578749503C200CB0000000000000000B0E697A5E69CACE8AA9E0A225CF09F9982CF7FFFFFFFFFFFFFFFC0C090",
        "96D92431313131313131312D313131312D313131312D313131312D313131313131313131313131D92432323232323232322D323232322D323232322D323232322D32323232323232323232323200D92433333333333333332D333333332D333333332D333333332D33333333333333333333333395A872656A6563746564C0C0A7696E76616C6964A76661696C75726590",
        "96D92431313131313131312D313131312D313131312D313131312D313131313131313131313131D92432323232323232322D323232322D323232322D323232322D32323232323232323232323200D92433333333333333332D333333332D333333332D333333332D33333333333333333333333395A8636F6E666C696374C02AC0C090",
        "96D92431313131313131312D313131312D313131312D313131312D313131313131313131313131D92432323232323232322D323232322D323232322D323232322D32323232323232323232323202C0C090",
    ];

    /// <summary>Checks all scalar kinds, integer limits, Unicode, empty batches and error results.</summary>
    /// <param name="variant">The message variant.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void DirectEncodersPreserveProtocol(int variant)
    {
        var fields = new Dictionary<string, DiagnosticValue>
        {
            ["flag"] = DiagnosticValue.From(true),
            ["integer"] = DiagnosticValue.From(long.MinValue),
            ["number"] = DiagnosticValue.From(-1.25),
            ["text"] = DiagnosticValue.From("日本語\n\"\\🙂"),
        };
        DiagnosticOperationResult? result = variant switch
        {
            1 => DiagnosticOperationResult.Success(fields, long.MaxValue),
            2 => DiagnosticOperationResult.Reject("invalid", "failure"),
            3 => DiagnosticOperationResult.Conflict(42),
            _ => null,
        };
        var message = new DiagnosticMessage(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), variant == 4 ? DiagnosticMessageKind.Heartbeat : result == null ? DiagnosticMessageKind.Telemetry : DiagnosticMessageKind.CommandResult, result == null ? null : Guid.Parse("33333333-3333-3333-3333-333333333333"), result, variant == 0 ? [new("log", long.MaxValue, "encoding", DiagnosticValue.From(9007199254740993L), "trace", "span", null, long.MinValue, fields)] : []);
        var buffer = new ArrayBufferWriter<byte>();
        DiagnosticJsonMessageEncoder.Write(buffer, message);
        byte[] baselineJson = JsonSerializer.SerializeToUtf8Bytes(message, DiagnosticJson.Context.DiagnosticMessage);
        Assert.Equal(baselineJson, buffer.WrittenSpan.ToArray());
        DiagnosticMessage decodedJson = JsonSerializer.Deserialize(buffer.WrittenSpan, DiagnosticJson.Context.DiagnosticMessage)!;
        Assert.Equal(baselineJson, JsonSerializer.SerializeToUtf8Bytes(decodedJson, DiagnosticJson.Context.DiagnosticMessage));

        byte[] baselinePack = Convert.FromHexString(_versionOnePublications[variant]);
        byte[] directPack = MessagePackSerializer.Serialize(message, DiagnosticMessagePack.Options);
        Assert.Equal(baselinePack, directPack);
        DiagnosticMessage decodedPack = MessagePackSerializer.Deserialize<DiagnosticMessage>(directPack, DiagnosticMessagePack.Options);
        Assert.Equal(baselineJson, JsonSerializer.SerializeToUtf8Bytes(decodedPack, DiagnosticJson.Context.DiagnosticMessage));
    }

    /// <summary>Checks unknown trailing fields remain readable without bypassing the depth limit.</summary>
    /// <param name="depth">The nesting depth of the unknown value.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    public void UnknownPublicationFieldsRespectDepth(int depth)
    {
        byte[] original = Convert.FromHexString(_versionOnePublications[4]);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(7);
        writer.WriteRaw(original.AsSpan(1));
        for (int index = 0; index < depth; index++)
        {
            if (index % 2 == 0)
            {
                writer.WriteMapHeader(1);
                writer.Write("future");
            }
            else
            {
                writer.WriteArrayHeader(1);
            }
        }

        writer.WriteNil();
        writer.Flush();
        if (depth >= 32)
        {
            Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<DiagnosticMessage>(buffer.WrittenMemory, DiagnosticMessagePack.Options));
        }
        else
        {
            DiagnosticMessage message = MessagePackSerializer.Deserialize<DiagnosticMessage>(buffer.WrittenMemory, DiagnosticMessagePack.Options);
            Assert.Equal(original, MessagePackSerializer.Serialize(message, DiagnosticMessagePack.Options));
        }
    }

    /// <summary>Checks malformed publications and duplicate field names cannot reach the registry.</summary>
    /// <param name="variant">The malformed token variant.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void MalformedPublicationsAreRejected(int variant)
    {
        byte[] original = Convert.FromHexString(_versionOnePublications[0]);
        var reader = new MessagePackReader(original);
        _ = reader.ReadArrayHeader();
        for (int index = 0; index < 5; index++)
        {
            reader.Skip();
        }

        int eventsOffset = (int)reader.Consumed;
        _ = reader.ReadArrayHeader();
        int eventOffset = (int)reader.Consumed;
        _ = reader.ReadArrayHeader();
        for (int index = 0; index < 8; index++)
        {
            reader.Skip();
        }

        int fieldsOffset = (int)reader.Consumed;
        byte[] malformed = variant switch
        {
            0 => [MessagePackCode.Nil],
            1 => [MessagePackCode.MinFixArray + 2, MessagePackCode.Nil, MessagePackCode.Nil],
            2 => [.. original.AsSpan(0, eventsOffset), MessagePackCode.Nil],
            3 => [.. original.AsSpan(0, eventOffset), MessagePackCode.Nil],
            4 => [.. original.AsSpan(0, fieldsOffset), MessagePackCode.Nil],
            _ => [.. original.AsSpan(0, fieldsOffset), MessagePackCode.MinFixMap + 5, .. original.AsSpan(fieldsOffset + 1), .. Convert.FromHexString("A4666C61679500C200CB0000000000000000C0")],
        };
        Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<DiagnosticMessage>(malformed, DiagnosticMessagePack.Options));
    }

    /// <summary>Checks collection headers are bounded before allocating the declared collection.</summary>
    /// <param name="result">Whether to encode a result map instead of events.</param>
    /// <param name="fields">Whether to encode an event field map.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void CollectionLimitsAreCheckedBeforeReadingEntries(bool result, bool fields)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(6);
        writer.Write("11111111-1111-1111-1111-111111111111");
        writer.Write("22222222-2222-2222-2222-222222222222");
        writer.Write((int)(result ? DiagnosticMessageKind.CommandResult : DiagnosticMessageKind.Telemetry));
        writer.WriteNil();
        if (result)
        {
            writer.WriteArrayHeader(5);
            writer.Write("success");
            writer.WriteMapHeader(33);
        }
        else
        {
            writer.WriteNil();
            writer.WriteArrayHeader(fields ? 1 : 129);
            if (fields)
            {
                writer.WriteArrayHeader(9);
                writer.Write("metric");
                writer.Write(0L);
                writer.Write("metric");
                writer.WriteArrayHeader(0);
                writer.WriteNil();
                writer.WriteNil();
                writer.WriteNil();
                writer.Write(0L);
                writer.WriteMapHeader(37);
            }
        }

        // Ensure the reader has enough bytes to inspect the declared header before rejecting its count.
        for (int index = 0; index < 300; index++)
        {
            writer.WriteNil();
        }

        writer.Flush();
        MessagePackSerializationException error = Assert.Throws<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<DiagnosticMessage>(buffer.WrittenMemory, DiagnosticMessagePack.Options));
        Assert.Contains(result || fields ? "field count" : "128 events", error.InnerException!.Message, StringComparison.Ordinal);
    }

    /// <summary>Checks schema validation and encoding do not materialize the dictionary compatibility view.</summary>
    [Fact]
    public void GeneratedStyleValuesStayDetachedAndWriteDirectly()
    {
        var output = new ProbeOutput("snapshot");
        var operations = new DiagnosticOperationSet(new ProbeContributor(output));
        DiagnosticOperationResult result = operations.Invoke("read", new Dictionary<string, DiagnosticValue>(), new(Guid.NewGuid(), Guid.NewGuid(), default, "actor", null, default), new HashSet<DiagnosticPermission> { DiagnosticPermission.Observe });
        Assert.Equal("success", result.Status);
        Assert.Equal(1, output.Writes);
        var message = new DiagnosticMessage(Guid.NewGuid(), Guid.NewGuid(), DiagnosticMessageKind.CommandResult, Guid.NewGuid(), result, []);
        var buffer = new ArrayBufferWriter<byte>();
        DiagnosticJsonMessageEncoder.Write(buffer, message);
        _ = MessagePackSerializer.Serialize(message, DiagnosticMessagePack.Options);
        Assert.Equal(3, output.Writes);
        Assert.Equal("snapshot", result.Values!["text"].String);
        Assert.Equal(4, output.Writes);
        Assert.Equal("snapshot", result.Values["text"].String);
        Assert.Equal(4, output.Writes);
    }

    /// <summary>Checks duplicate names and invalid scalar output remain rejected on the direct path.</summary>
    /// <param name="invalid">The invalid output mode.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DirectOutputValidationRejectsInvalidValues(int invalid)
    {
        var operations = new DiagnosticOperationSet(new ProbeContributor(new ProbeOutput("snapshot", invalid)));
        DiagnosticOperationResult result = operations.Invoke("read", new Dictionary<string, DiagnosticValue>(), new(Guid.NewGuid(), Guid.NewGuid(), default, "actor", null, default), new HashSet<DiagnosticPermission> { DiagnosticPermission.Observe });
        Assert.Equal("invalid-result", result.Code);
    }

    private sealed class ProbeContributor(ProbeOutput output) : IDiagnosticContributor
    {
        public void Configure(DiagnosticBuilder builder) => builder.Operation(new("read", "Read", DiagnosticPermission.Observe, [], [new("text", DiagnosticValueKind.String), new("number", DiagnosticValueKind.Double)]), (_, _) => DiagnosticOperationResult.Success(output));
    }

    private sealed class ProbeOutput(string text, int invalid = 0) : DiagnosticOutputValues
    {
        public override int Count => 2;

        public int Writes { get; private set; }

        public override void WriteTo<TWriter>(ref TWriter writer)
        {
            Writes++;
            writer.Write("text", invalid == 1 ? null! : text);
            writer.Write(invalid == 2 ? "text" : "number", invalid == 3 ? double.NaN : 1.25);
        }
    }
}
