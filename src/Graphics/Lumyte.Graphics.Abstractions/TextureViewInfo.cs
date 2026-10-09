namespace Lumyte.Graphics.Abstractions;

/// <summary>Contains immutable, normalized view attributes; counts never contain sentinels.</summary>
public sealed record TextureViewInfo
{
    /// <summary>Gets the source storage format.</summary>
    public required TextureFormat Format { get; init; }

    /// <summary>Gets the resolved view dimension.</summary>
    public required TextureViewDimension Dimension { get; init; }

    /// <summary>Gets the first selected mip.</summary>
    public required uint BaseMipLevel { get; init; }

    /// <summary>Gets the positive resolved mip count.</summary>
    public required uint MipLevelCount { get; init; }

    /// <summary>Gets the first selected array layer.</summary>
    public required uint BaseArrayLayer { get; init; }

    /// <summary>Gets the positive resolved layer count.</summary>
    public required uint ArrayLayerCount { get; init; }
}
