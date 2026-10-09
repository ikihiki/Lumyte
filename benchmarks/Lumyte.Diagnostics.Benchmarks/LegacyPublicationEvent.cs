using MessagePack;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Benchmark-only version-one wire schema for LegacyPublicationEvent.</summary>
[MessagePackObject]
public sealed partial class LegacyPublicationEvent
{
    /// <summary>Gets or sets the Kind field.</summary>
    [Key(0)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets the Timestamp field.</summary>
    [Key(1)]
    public long Timestamp { get; set; }

    /// <summary>Gets or sets the Name field.</summary>
    [Key(2)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the Value field.</summary>
    [Key(3)]
    public LegacyPublicationValue Value { get; set; } = new();

    /// <summary>Gets or sets the TraceId field.</summary>
    [Key(4)]
    public string? TraceId { get; set; }

    /// <summary>Gets or sets the SpanId field.</summary>
    [Key(5)]
    public string? SpanId { get; set; }

    /// <summary>Gets or sets the ParentSpanId field.</summary>
    [Key(6)]
    public string? ParentSpanId { get; set; }

    /// <summary>Gets or sets the DurationTicks field.</summary>
    [Key(7)]
    public long DurationTicks { get; set; }

    /// <summary>Gets or sets the Fields field.</summary>
    [Key(8)]
    public Dictionary<string, LegacyPublicationValue> Fields { get; set; } = [];
}
