using MessagePack;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Version-one numeric-key wire schema for WireWelcome.</summary>
[MessagePackObject]
public sealed partial class WireWelcome
{
    /// <summary>Gets or sets the SessionId field.</summary>
    [Key(0)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Gets or sets the SessionSecret field.</summary>
    [Key(1)]
    public string SessionSecret { get; set; } = string.Empty;

    /// <summary>Gets or sets the Permissions field.</summary>
    [Key(2)]
    public int[] Permissions { get; set; } = [];
}
