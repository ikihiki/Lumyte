namespace Lumyte.Graphics.Abstractions;

/// <summary>Identifies an explicit nonempty mip and layer range.</summary>
/// <param name="BaseMipLevel">The first mip.</param>
/// <param name="MipLevelCount">The positive mip count.</param>
/// <param name="BaseArrayLayer">The first layer.</param>
/// <param name="ArrayLayerCount">The positive layer count.</param>
public readonly record struct TextureSubresourceRange(uint BaseMipLevel, uint MipLevelCount, uint BaseArrayLayer, uint ArrayLayerCount);
