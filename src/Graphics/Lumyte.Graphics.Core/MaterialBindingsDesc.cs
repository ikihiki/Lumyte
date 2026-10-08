namespace Lumyte.Graphics;
/// <summary>
/// Describes a finite sampled-resource set for one reflected material schema.
/// </summary>
public sealed record MaterialBindingsDesc
{
    /// <summary>
    /// Gets the reflected layout obtained from the drawing shader.
    /// </summary>
    public required MaterialResourceLayout Layout { get; init; }

    /// <summary>
    /// Gets a valid sampled pair used for unused slots; it counts toward capacity.
    /// </summary>
    public required SampledTexture2DReference UnusedSlotFallback { get; init; }
}
