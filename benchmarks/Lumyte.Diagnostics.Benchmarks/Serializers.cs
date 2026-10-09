using System.Text.Json;
using MemoryPack;
using MessagePack;
using MessagePack.Resolvers;
using ProtoBuf;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Matched byte-array encode and typed decode APIs; setup costs are excluded.</summary>
public static class Serializers
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { Converters = { new LongJsonConverter() } };
    private static readonly BenchmarkJsonContext _json = new(_jsonOptions);
    private static readonly MessagePackSerializerOptions _messagePack = MessagePackSerializerOptions.Standard
        .WithResolver(CompositeResolver.Create(BenchmarkMessagePackResolver.Instance, StandardResolver.Instance));

    /// <summary>Encodes JSON with generated metadata.</summary>
    /// <param name="batch">The batch argument.</param>
    /// <returns>The computed result.</returns>
    public static byte[] JsonEncode(WireBatch batch) => JsonSerializer.SerializeToUtf8Bytes(batch, _json.WireBatch);

    /// <summary>Decodes JSON with generated metadata.</summary>
    /// <param name="bytes">The bytes argument.</param>
    /// <returns>The computed result.</returns>
    public static WireBatch JsonDecode(byte[] bytes) => JsonSerializer.Deserialize(bytes, _json.WireBatch)!;

    /// <summary>Encodes MessagePack with generated formatters.</summary>
    /// <param name="batch">The batch argument.</param>
    /// <returns>The computed result.</returns>
    public static byte[] MessagePackEncode(WireBatch batch) => MessagePackSerializer.Serialize(batch, _messagePack);

    /// <summary>Decodes MessagePack with generated formatters.</summary>
    /// <param name="bytes">The bytes argument.</param>
    /// <returns>The computed result.</returns>
    public static WireBatch MessagePackDecode(byte[] bytes) => MessagePackSerializer.Deserialize<WireBatch>(bytes, _messagePack);

    /// <summary>Encodes MemoryPack with generated formatters.</summary>
    /// <param name="batch">The batch argument.</param>
    /// <returns>The computed result.</returns>
    public static byte[] MemoryPackEncode(WireBatch batch) => MemoryPackSerializer.Serialize(batch);

    /// <summary>Decodes MemoryPack with generated formatters.</summary>
    /// <param name="bytes">The bytes argument.</param>
    /// <returns>The computed result.</returns>
    public static WireBatch MemoryPackDecode(byte[] bytes) => MemoryPackSerializer.Deserialize<WireBatch>(bytes)!;

    /// <summary>Encodes protobuf-net using its typed schema.</summary>
    /// <param name="batch">The batch argument.</param>
    /// <returns>The computed result.</returns>
    public static byte[] ProtobufEncode(WireBatch batch)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, batch);
        return stream.ToArray();
    }

    /// <summary>Decodes protobuf-net using its typed schema.</summary>
    /// <param name="bytes">The bytes argument.</param>
    /// <returns>The computed result.</returns>
    public static WireBatch ProtobufDecode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return Serializer.Deserialize<WireBatch>(stream);
    }
}
