using MessagePack;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Version-one numeric-key wire schema for WireCommand.</summary>
[MessagePackObject]
public sealed partial class WireCommand
{
    /// <summary>Gets or sets the RequestId field.</summary>
    [Key(0)]
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Gets or sets the SubsystemId field.</summary>
    [Key(1)]
    public string SubsystemId { get; set; } = string.Empty;

    /// <summary>Gets or sets the OperationId field.</summary>
    [Key(2)]
    public string OperationId { get; set; } = string.Empty;

    /// <summary>Gets or sets the ActorId field.</summary>
    [Key(3)]
    public string ActorId { get; set; } = string.Empty;

    /// <summary>Gets or sets the ExpectedRevision field.</summary>
    [Key(4)]
    public long? ExpectedRevision { get; set; }

    /// <summary>Gets or sets the ExpiresUnixMilliseconds field.</summary>
    [Key(5)]
    public long ExpiresUnixMilliseconds { get; set; }

    /// <summary>Gets or sets the Arguments field.</summary>
    [Key(6)]
    public Dictionary<string, WireValue> Arguments { get; set; } = [];
}
