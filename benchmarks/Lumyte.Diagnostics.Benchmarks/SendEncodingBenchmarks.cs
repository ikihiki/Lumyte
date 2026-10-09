using System.Buffers;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.MagicOnion;
using MessagePack;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Compares actual publication encoding, including the previous MessagePack DTO mapping.</summary>
[MemoryDiagnoser]
public class SendEncodingBenchmarks
{
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private DiagnosticMessage _message = null!;

    /// <summary>Gets or sets the number of collected events.</summary>
    [Params(1, 128)]
    public int EventCount { get; set; }

    /// <summary>Creates detached events and verifies exact protocol equivalence before measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var fields = new Dictionary<string, DiagnosticValue>
        {
            ["instance"] = DiagnosticValue.From("benchmark"),
            ["enabled"] = DiagnosticValue.From(true),
            ["frame"] = DiagnosticValue.From(9007199254740993L),
            ["elapsed"] = DiagnosticValue.From(1.25),
        };
        DiagnosticEvent[] events = Enumerable.Range(0, EventCount).Select(index => new DiagnosticEvent("metric", index, "frame.duration", DiagnosticValue.From(1.25), null, null, null, 0, fields)).ToArray();
        _message = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), DiagnosticMessageKind.Telemetry, null, null, events);
        if (!JsonSourceGenerated().AsSpan().SequenceEqual(JsonDirect()) || !MessagePackDto().AsSpan().SequenceEqual(MessagePackDirect()))
        {
            throw new InvalidOperationException("Direct encoding changed the wire schema.");
        }
    }

    /// <summary>Encodes using the previous source-generated JSON publication path.</summary>
    /// <returns>An owned payload.</returns>
    [Benchmark]
    public byte[] JsonSourceGenerated() => JsonSerializer.SerializeToUtf8Bytes(_message, DiagnosticJson.Context.DiagnosticMessage);

    /// <summary>Writes JSON directly, then copies to an owned payload for equal output ownership.</summary>
    /// <returns>An owned payload.</returns>
    [Benchmark]
    public byte[] JsonDirect()
    {
        _buffer.Clear();
        DiagnosticJsonMessageEncoder.Write(_buffer, _message);
        return _buffer.WrittenSpan.ToArray();
    }

    /// <summary>Maps every event and field to wire DTOs before serializing.</summary>
    /// <returns>An owned payload.</returns>
    [Benchmark]
    public byte[] MessagePackDto() => MessagePackSerializer.Serialize(LegacyPublication.Map(_message), LegacyPublication.Options);

    /// <summary>Uses the publication formatter without constructing per-event wire DTOs.</summary>
    /// <returns>An owned payload.</returns>
    [Benchmark]
    public byte[] MessagePackDirect() => MessagePackSerializer.Serialize(_message, DiagnosticMessagePack.Options);
}
