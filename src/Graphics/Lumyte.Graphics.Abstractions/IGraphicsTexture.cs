namespace Lumyte.Graphics.Abstractions;

/// <summary>Exposes an owned allocation or a borrowed frame image; dispose views before releasing its owner.</summary>
/// <remarks>Concurrent operations are not synchronized; the caller manages all required resource lifetime and access synchronization.</remarks>
public interface IGraphicsTexture : IDisposable
{
    /// <summary>Gets the base width in texels.</summary>
    uint Width { get; }

    /// <summary>Gets the base height in texels.</summary>
    uint Height { get; }

    /// <summary>Gets the allocated mip count.</summary>
    uint MipLevels { get; }

    /// <summary>Gets the immutable array layer count.</summary>
    uint ArrayLayers { get; }

    /// <summary>Gets the exact storage format.</summary>
    TextureFormat Format { get; }

    /// <summary>Gets the immutable permitted usages.</summary>
    TextureUsage Usage { get; }

    /// <summary>Gets the selected mip dimensions without changing the array layer count.</summary>
    /// <param name="mipLevel">The zero-based mip level within the allocation.</param>
    /// <returns>The positive mip width and height in texels.</returns>
    (uint Width, uint Height) GetMipSize(uint mipLevel);

    /// <summary>Creates an owned view in the source format; null selects the full default view.</summary>
    /// <param name="desc">The requested ranges and dimension, or null for D2 or D2Array according to layer count.</param>
    /// <returns>The backend view; the caller keeps the texture alive until all views are released.</returns>
    IGraphicsTextureView CreateView(TextureViewDesc? desc = null);
}
