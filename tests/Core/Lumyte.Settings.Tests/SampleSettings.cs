namespace Lumyte.Settings.Tests;

/// <summary>A sample module with nested objects, dictionaries and arrays.</summary>
public sealed class SampleSettings
{
    /// <summary>Gets or sets grouped entries.</summary>
    public Dictionary<string, Dictionary<string, string[]>> Entries { get; set; } = [];

    /// <summary>Gets or sets the primary range.</summary>
    public SampleRange PrimaryRange { get; set; } = new();

    /// <summary>Gets or sets the secondary range.</summary>
    public SampleRange SecondaryRange { get; set; } = new();
}
