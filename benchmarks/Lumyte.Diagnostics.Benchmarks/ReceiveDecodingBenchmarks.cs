using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.MagicOnion;
using MessagePack;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Measures server-side publication decoding with and without the superseded wire DTO graph.</summary>
[MemoryDiagnoser]
public class ReceiveDecodingBenchmarks
{
    private byte[] _payload = null!;

    /// <summary>Gets or sets the number of received events.</summary>
    [Params(1, 128)]
    public int EventCount { get; set; }

    /// <summary>Creates identical version-one payloads and verifies the decoded values agree.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var fixture = new SendEncodingBenchmarks { EventCount = EventCount };
        fixture.Setup();
        _payload = fixture.MessagePackDirect();
        byte[] legacy = JsonSerializer.SerializeToUtf8Bytes(MessagePackDto(), DiagnosticJson.Context.DiagnosticMessage);
        byte[] direct = JsonSerializer.SerializeToUtf8Bytes(MessagePackDirect(), DiagnosticJson.Context.DiagnosticMessage);
        if (!legacy.AsSpan().SequenceEqual(direct))
        {
            throw new InvalidOperationException("Direct decoding changed the publication values.");
        }
    }

    /// <summary>Decodes a wire DTO graph and then maps it to common diagnostic values.</summary>
    /// <returns>The detached publication.</returns>
    [Benchmark(Baseline = true)]
    public DiagnosticMessage MessagePackDto() => LegacyPublication.Map(MessagePackSerializer.Deserialize<LegacyPublicationMessage>(_payload, LegacyPublication.Options));

    /// <summary>Decodes common diagnostic values directly from the wire fields.</summary>
    /// <returns>The detached publication.</returns>
    [Benchmark]
    public DiagnosticMessage MessagePackDirect() => MessagePackSerializer.Deserialize<DiagnosticMessage>(_payload, DiagnosticMessagePack.Options);
}
