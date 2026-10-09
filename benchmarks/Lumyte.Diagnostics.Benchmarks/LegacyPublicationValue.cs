using MessagePack;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Benchmark-only version-one wire schema for LegacyPublicationValue.</summary>
[MessagePackObject]
public sealed partial class LegacyPublicationValue
{
    /// <summary>Gets or sets the Kind field.</summary>
    [Key(0)]
    public int Kind { get; set; }

    /// <summary>Gets or sets a value indicating whether Boolean is enabled.</summary>
    [Key(1)]
    public bool Boolean { get; set; }

    /// <summary>Gets or sets the Integer field.</summary>
    [Key(2)]
    public long Integer { get; set; }

    /// <summary>Gets or sets the Number field.</summary>
    [Key(3)]
    public double Number { get; set; }

    /// <summary>Gets or sets the Text field.</summary>
    [Key(4)]
    public string? Text { get; set; }
}
