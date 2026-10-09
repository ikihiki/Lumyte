namespace Lumyte.Graphics.Abstractions;

/// <summary>Selects a view dimension and mip and layer ranges without allocating resources.</summary>
public sealed record TextureViewDesc
{
    /// <summary>Gets the view dimension; cube views require square faces.</summary>
    public TextureViewDimension Dimension { get; init; } = TextureViewDimension.D2;

    /// <summary>Gets the first mip level.</summary>
    public uint BaseMipLevel { get; init; }

    /// <summary>Gets the positive mip count, or null for all remaining levels.</summary>
    public uint? MipLevelCount { get; init; }

    /// <summary>Gets the first array layer.</summary>
    public uint BaseArrayLayer { get; init; }

    /// <summary>Gets the positive layer count, or null for the dimension-specific default.</summary>
    public uint? ArrayLayerCount { get; init; }
}
