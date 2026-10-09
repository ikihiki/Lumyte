using MessagePack;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Version-one numeric-key wire schema for WireResult.</summary>
[MessagePackObject]
public sealed partial class WireResult
{
    /// <summary>Gets or sets the Status field.</summary>
    [Key(0)]
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the Values field.</summary>
    [Key(1)]
    public Dictionary<string, WireValue>? Values { get; set; }

    /// <summary>Gets or sets the Revision field.</summary>
    [Key(2)]
    public long? Revision { get; set; }

    /// <summary>Gets or sets the Code field.</summary>
    [Key(3)]
    public string? Code { get; set; }

    /// <summary>Gets or sets the Message field.</summary>
    [Key(4)]
    public string? Message { get; set; }
}
