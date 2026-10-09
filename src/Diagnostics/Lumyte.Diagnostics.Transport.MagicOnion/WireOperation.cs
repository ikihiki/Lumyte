using MessagePack;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Version-one numeric-key wire schema for WireOperation.</summary>
[MessagePackObject]
public sealed partial class WireOperation
{
    /// <summary>Gets or sets the Id field.</summary>
    [Key(0)]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the DisplayName field.</summary>
    [Key(1)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the Permission field.</summary>
    [Key(2)]
    public int Permission { get; set; }

    /// <summary>Gets or sets the Arguments field.</summary>
    [Key(3)]
    public WireField[] Arguments { get; set; } = [];

    /// <summary>Gets or sets the Results field.</summary>
    [Key(4)]
    public WireField[] Results { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether RequiresRevision is enabled.</summary>
    [Key(5)]
    public bool RequiresRevision { get; set; }
}
