using MessagePack;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Version-one numeric-key wire schema for WireReceipt.</summary>
[MessagePackObject]
public sealed partial class WireReceipt
{
    /// <summary>Gets or sets the MessageId field.</summary>
    [Key(0)]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether Accepted is enabled.</summary>
    [Key(1)]
    public bool Accepted { get; set; }

    /// <summary>Gets or sets the ErrorCode field.</summary>
    [Key(2)]
    public string? ErrorCode { get; set; }
}
