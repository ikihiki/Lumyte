using MessagePack;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Benchmark-only version-one wire schema for LegacyPublicationResult.</summary>
[MessagePackObject]
public sealed partial class LegacyPublicationResult
{
    /// <summary>Gets or sets the Status field.</summary>
    [Key(0)]
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the Values field.</summary>
    [Key(1)]
    public Dictionary<string, LegacyPublicationValue>? Values { get; set; }

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
