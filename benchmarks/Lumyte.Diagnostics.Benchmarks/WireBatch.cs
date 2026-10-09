using MemoryPack;
using MessagePack;
using ProtoBuf;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Identical explicit wire shape used by every serializer.</summary>
[MemoryPackable]
[MessagePackObject]
[ProtoContract]
public sealed partial class WireBatch
{
    /// <summary>Gets or sets SessionId.</summary>
    [Key(0)]
    [ProtoMember(1)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Gets or sets Sequence.</summary>
    [Key(1)]
    [ProtoMember(2)]
    public long Sequence { get; set; }

    /// <summary>Gets or sets Events.</summary>
    [Key(2)]
    [ProtoMember(3)]
    public WireEvent[] Events { get; set; } = [];

    /// <summary>Gets or sets Binary.</summary>
    [Key(3)]
    [ProtoMember(4)]
    public byte[] Binary { get; set; } = [];
}
