namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes an exact color texture copy region.</summary>
public sealed record TextureCopyRegion
{
    /// <summary>Gets the source allocation.</summary>
    public required IGraphicsTexture Texture { get; init; }

    /// <summary>Gets the selected mip.</summary>
    public uint MipLevel { get; init; }

    /// <summary>Gets the horizontal texel offset.</summary>
    public uint OriginX { get; init; }

    /// <summary>Gets the vertical texel offset.</summary>
    public uint OriginY { get; init; }

    /// <summary>Gets the first layer.</summary>
    public uint BaseArrayLayer { get; init; }

    /// <summary>Gets the positive width.</summary>
    public required uint Width { get; init; }

    /// <summary>Gets the positive height.</summary>
    public required uint Height { get; init; }

    /// <summary>Gets the positive layer count.</summary>
    public uint ArrayLayerCount { get; init; } = 1;
}
