namespace Lumyte.Graphics;

/// <summary>
/// Owns a texture allocation implemented directly by the backend.
/// </summary>
public interface IGraphicsTexture : IDisposable
{
    /// <summary>
    /// Gets the image width in texels.
    /// </summary>
    uint Width { get; }

    /// <summary>
    /// Gets the image height in texels.
    /// </summary>
    uint Height { get; }

    /// <summary>
    /// Gets the immutable permitted image usages.
    /// </summary>
    TextureUsage Usage { get; }

    /// <summary>
    /// Gets the image storage format.
    /// </summary>
    TextureFormat Format { get; }

    /// <summary>
    /// Creates an owned view covering the single mip and layer and retaining the texture.
    /// </summary>
    /// <returns>The owned view, which retains its source texture.</returns>
    IGraphicsTextureView CreateView();
}
