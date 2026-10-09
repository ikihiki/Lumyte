using MemoryPack;
using MessagePack;
using ProtoBuf;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Identical explicit wire shape used by every serializer.</summary>
[MemoryPackable]
[MessagePackObject]
[ProtoContract]
public sealed partial class WireEvent
{
    /// <summary>Gets or sets Kind.</summary>
    [Key(0)]
    [ProtoMember(1)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets Timestamp.</summary>
    [Key(1)]
    [ProtoMember(2)]
    public long Timestamp { get; set; }

    /// <summary>Gets or sets Name.</summary>
    [Key(2)]
    [ProtoMember(3)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets Value.</summary>
    [Key(3)]
    [ProtoMember(4, IsRequired = true)]
    public WireScalar Value { get; set; } = new();

    /// <summary>Gets or sets TraceId.</summary>
    [Key(4)]
    [ProtoMember(5)]
    public string? TraceId { get; set; }

    /// <summary>Gets or sets SpanId.</summary>
    [Key(5)]
    [ProtoMember(6)]
    public string? SpanId { get; set; }

    /// <summary>Gets or sets ParentSpanId.</summary>
    [Key(6)]
    [ProtoMember(7)]
    public string? ParentSpanId { get; set; }

    /// <summary>Gets or sets DurationTicks.</summary>
    [Key(7)]
    [ProtoMember(8)]
    public long DurationTicks { get; set; }

    /// <summary>Gets or sets Fields.</summary>
    [Key(8)]
    [ProtoMember(9)]
    public WireField[] Fields { get; set; } = [];
}
