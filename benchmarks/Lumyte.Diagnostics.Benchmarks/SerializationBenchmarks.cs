using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Warm steady-state encode/decode of equivalent diagnostic batches.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SerializationBenchmarks
{
    private WireBatch _batch = null!;
    private byte[] _json = null!;
    private byte[] _messagePack = null!;
    private byte[] _memoryPack = null!;
    private byte[] _protobuf = null!;

    /// <summary>Gets or sets the workload.</summary>
    [ParamsAllValues]
    public Workload Workload { get; set; }

    /// <summary>Initializes and verifies every serializer outside measured code.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _batch = Fixtures.Create(Workload);
        _json = Serializers.JsonEncode(_batch);
        _messagePack = Serializers.MessagePackEncode(_batch);
        _memoryPack = Serializers.MemoryPackEncode(_batch);
        _protobuf = Serializers.ProtobufEncode(_batch);
        WireBatch[] decoded = [Serializers.JsonDecode(_json), Serializers.MessagePackDecode(_messagePack), Serializers.MemoryPackDecode(_memoryPack), Serializers.ProtobufDecode(_protobuf)];
        foreach (WireBatch value in decoded)
        {
            if (!_json.AsSpan().SequenceEqual(Serializers.JsonEncode(value)))
            {
                throw new InvalidOperationException("Serializer round trip lost data.");
            }
        }
    }

    /// <summary>Encodes JSON.</summary>
    /// <returns>The computed result.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Encode")]
    public byte[] JsonEncode() => Serializers.JsonEncode(_batch);

    /// <summary>Encodes MessagePack.</summary>
    /// <returns>The computed result.</returns>
    [Benchmark]
    [BenchmarkCategory("Encode")]
    public byte[] MessagePackEncode() => Serializers.MessagePackEncode(_batch);

    /// <summary>Encodes MemoryPack.</summary>
    /// <returns>The computed result.</returns>
    [Benchmark]
    [BenchmarkCategory("Encode")]
    public byte[] MemoryPackEncode() => Serializers.MemoryPackEncode(_batch);

    /// <summary>Encodes protobuf-net.</summary>
    /// <returns>The computed result.</returns>
    [Benchmark]
    [BenchmarkCategory("Encode")]
    public byte[] ProtobufEncode() => Serializers.ProtobufEncode(_batch);

    /// <summary>Decodes JSON.</summary>
    /// <returns>The computed result.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Decode")]
    public WireBatch JsonDecode() => Serializers.JsonDecode(_json);

    /// <summary>Decodes MessagePack.</summary>
    /// <returns>The computed result.</returns>
    [Benchmark]
    [BenchmarkCategory("Decode")]
    public WireBatch MessagePackDecode() => Serializers.MessagePackDecode(_messagePack);

    /// <summary>Decodes MemoryPack.</summary>
    /// <returns>The computed result.</returns>
    [Benchmark]
    [BenchmarkCategory("Decode")]
    public WireBatch MemoryPackDecode() => Serializers.MemoryPackDecode(_memoryPack);

    /// <summary>Decodes protobuf-net.</summary>
    /// <returns>The computed result.</returns>
    [Benchmark]
    [BenchmarkCategory("Decode")]
    public WireBatch ProtobufDecode() => Serializers.ProtobufDecode(_protobuf);
}
