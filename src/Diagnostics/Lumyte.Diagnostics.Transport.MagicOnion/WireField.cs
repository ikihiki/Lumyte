using MessagePack;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Version-one numeric-key wire schema for WireField.</summary>
[MessagePackObject]
public sealed partial class WireField
{
    /// <summary>Gets or sets the Id field.</summary>
    [Key(0)]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the Kind field.</summary>
    [Key(1)]
    public int Kind { get; set; }

    /// <summary>Gets or sets the Minimum field.</summary>
    [Key(2)]
    public double? Minimum { get; set; }

    /// <summary>Gets or sets the Maximum field.</summary>
    [Key(3)]
    public double? Maximum { get; set; }

    /// <summary>Gets or sets the MaxLength field.</summary>
    [Key(4)]
    public int? MaxLength { get; set; }
}
