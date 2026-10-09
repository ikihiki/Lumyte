using MemoryPack;
using MessagePack;
using ProtoBuf;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Identical explicit wire shape used by every serializer.</summary>
[MemoryPackable]
[MessagePackObject]
[ProtoContract]
public sealed partial class WireScalar
{
    /// <summary>Gets or sets Kind.</summary>
    [Key(0)]
    [ProtoMember(1)]
    public int Kind { get; set; }

    /// <summary>Gets or sets a value indicating whether gets or sets Boolean.</summary>
    [Key(1)]
    [ProtoMember(2)]
    public bool Boolean { get; set; }

    /// <summary>Gets or sets Integer.</summary>
    [Key(2)]
    [ProtoMember(3)]
    public long Integer { get; set; }

    /// <summary>Gets or sets Number.</summary>
    [Key(3)]
    [ProtoMember(4)]
    public double Number { get; set; }

    /// <summary>Gets or sets Text.</summary>
    [Key(4)]
    [ProtoMember(5)]
    public string? Text { get; set; }
}
