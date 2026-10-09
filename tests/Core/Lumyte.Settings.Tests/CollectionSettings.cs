namespace Lumyte.Settings.Tests;

/// <summary>Exercises automatic copying of collections and nullable scalars.</summary>
public sealed class CollectionSettings
{
    /// <summary>Gets or sets a list of nested objects.</summary>
    public List<SampleRange> Ranges { get; set; } = [];

    /// <summary>Gets or sets an array of nested objects.</summary>
    public SampleRange[] Array { get; set; } = [];

    /// <summary>Gets or sets binary data handled by the built-in JSON converter.</summary>
    public byte[] Bytes { get; set; } = [];

    /// <summary>Gets or sets a nullable floating-point value.</summary>
    public float? Optional { get; set; }
}
