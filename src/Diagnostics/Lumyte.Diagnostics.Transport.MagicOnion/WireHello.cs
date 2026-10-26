using MessagePack;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Version-one numeric-key wire schema for WireHello.</summary>
[MessagePackObject]
public sealed partial class WireHello
{
    /// <summary>Gets or sets the InstanceId field.</summary>
    [Key(0)]
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the ProtocolVersion field.</summary>
    [Key(1)]
    public int ProtocolVersion { get; set; }

    /// <summary>Gets or sets the Catalog field.</summary>
    [Key(2)]
    public WireSubsystem[] Catalog { get; set; } = [];
}
