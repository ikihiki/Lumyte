namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns a backend texture view; the caller keeps its source allocation alive.</summary>
/// <remarks>Concurrent operations are not synchronized; the caller manages all required resource lifetime and access synchronization.</remarks>
public interface IGraphicsTextureView : IDisposable
{
    /// <summary>Gets the source texture, which the caller must keep alive while the view is in use.</summary>
    IGraphicsTexture Texture { get; }

    /// <summary>Gets the normalized dimension, format and subresource range.</summary>
    TextureViewInfo Info { get; }
}
