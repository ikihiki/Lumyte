namespace Lumyte.Graphics;

/// <summary>
/// Owns a backend view that retains its source texture.
/// </summary>
public interface IGraphicsTextureView : IDisposable
{
    /// <summary>
    /// Gets the retained source texture.
    /// </summary>
    IGraphicsTexture Texture { get; }
}
