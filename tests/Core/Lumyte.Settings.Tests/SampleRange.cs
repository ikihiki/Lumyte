namespace Lumyte.Settings.Tests;

/// <summary>A nested object used to exercise default completion and validation.</summary>
public sealed class SampleRange
{
    /// <summary>Gets or sets the minimum value.</summary>
    public float Minimum { get; set; } = 0.15f;

    /// <summary>Gets or sets the maximum value.</summary>
    public float Maximum { get; set; } = 1;
}
