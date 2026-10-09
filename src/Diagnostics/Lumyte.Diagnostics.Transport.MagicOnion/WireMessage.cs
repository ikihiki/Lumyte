using MessagePack;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Version-one numeric-key wire schema for WireMessage.</summary>
[MessagePackObject]
public sealed partial class WireMessage
{
    /// <summary>Gets or sets the MessageId field.</summary>
    [Key(0)]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Gets or sets the SessionId field.</summary>
    [Key(1)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Gets or sets the Kind field.</summary>
    [Key(2)]
    public int Kind { get; set; }

    /// <summary>Gets or sets the RequestId field.</summary>
    [Key(3)]
    public string? RequestId { get; set; }

    /// <summary>Gets or sets the Result field.</summary>
    [Key(4)]
    public WireResult? Result { get; set; }

    /// <summary>Gets or sets the Events field.</summary>
    [Key(5)]
    public WireEvent[] Events { get; set; } = [];
}
