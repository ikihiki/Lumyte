using MemoryPack;
using MessagePack;
using ProtoBuf;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Identical explicit wire shape used by every serializer.</summary>
[MemoryPackable]
[MessagePackObject]
[ProtoContract]
public sealed partial class WireField
{
    /// <summary>Gets or sets Name.</summary>
    [Key(0)]
    [ProtoMember(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets Value.</summary>
    [Key(1)]
    [ProtoMember(2, IsRequired = true)]
    public WireScalar Value { get; set; } = new();
}
