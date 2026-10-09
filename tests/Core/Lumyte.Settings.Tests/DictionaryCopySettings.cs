namespace Lumyte.Settings.Tests;

/// <summary>Exercises copying dictionaries with reference and value type entries.</summary>
public sealed class DictionaryCopySettings
{
    /// <summary>Gets or sets dictionaries containing mutable values.</summary>
    public Dictionary<string, SampleRange> Entries { get; set; } = [];

    /// <summary>Gets or sets dictionaries containing scalar values.</summary>
    public Dictionary<string, int> Counts { get; set; } = [];
}
