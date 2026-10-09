namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns a backend texture view and retains its source allocation.</summary>
/// <remarks>Concurrent operations are not synchronized; the caller manages all required resource lifetime and access synchronization.</remarks>
public interface IGraphicsTextureView : IDisposable
{
    /// <summary>Gets the retained source texture; disposal is refused while views remain alive.</summary>
    IGraphicsTexture Texture { get; }

    /// <summary>Gets the normalized dimension, format and subresource range.</summary>
    TextureViewInfo Info { get; }
}
