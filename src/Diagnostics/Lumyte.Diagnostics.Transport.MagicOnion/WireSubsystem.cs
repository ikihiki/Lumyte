using MessagePack;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Version-one numeric-key wire schema for WireSubsystem.</summary>
[MessagePackObject]
public sealed partial class WireSubsystem
{
    /// <summary>Gets or sets the Id field.</summary>
    [Key(0)]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the DisplayName field.</summary>
    [Key(1)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the SchemaVersion field.</summary>
    [Key(2)]
    public int SchemaVersion { get; set; }

    /// <summary>Gets or sets the Operations field.</summary>
    [Key(3)]
    public WireOperation[] Operations { get; set; } = [];
}
