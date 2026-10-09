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
        var message = new DiagnosticMessage(Guid.NewGuid(), Guid.NewGuid(), variant == 4 ? DiagnosticMessageKind.Heartbeat : result == null ? DiagnosticMessageKind.Telemetry : DiagnosticMessageKind.CommandResult, result == null ? null : Guid.NewGuid(), result, variant == 0 ? [new("log", long.MaxValue, "encoding", DiagnosticValue.From(9007199254740993L), "trace", "span", null, long.MinValue, fields)] : []);
        var buffer = new ArrayBufferWriter<byte>();
        DiagnosticJsonMessageEncoder.Write(buffer, message);
        byte[] baselineJson = JsonSerializer.SerializeToUtf8Bytes(message, DiagnosticJson.Context.DiagnosticMessage);
        Assert.Equal(baselineJson, buffer.WrittenSpan.ToArray());
        DiagnosticMessage decodedJson = JsonSerializer.Deserialize(buffer.WrittenSpan, DiagnosticJson.Context.DiagnosticMessage)!;
        Assert.Equal(baselineJson, JsonSerializer.SerializeToUtf8Bytes(decodedJson, DiagnosticJson.Context.DiagnosticMessage));

        byte[] baselinePack = MessagePackSerializer.Serialize(WireMapper.ToWire(message), DiagnosticMessagePack.Options);
        byte[] directPack = MessagePackSerializer.Serialize(message, DiagnosticMessagePack.Options);
        Assert.Equal(baselinePack, directPack);
        DiagnosticMessage decodedPack = MessagePackSerializer.Deserialize<DiagnosticMessage>(directPack, DiagnosticMessagePack.Options);
        Assert.Equal(baselineJson, JsonSerializer.SerializeToUtf8Bytes(decodedPack, DiagnosticJson.Context.DiagnosticMessage));
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
