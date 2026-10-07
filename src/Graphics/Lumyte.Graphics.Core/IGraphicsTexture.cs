namespace Lumyte.Graphics;

/// <summary>A texture allocation directly owned by its backend implementation.</summary>
public interface IGraphicsTexture : IDisposable
{
    uint Width { get; }
    uint Height { get; }
    /// <summary>Creates an owned native view. The texture is held until the view is disposed.</summary>
    IGraphicsTextureView CreateView();
}

/// <summary>An owned view retaining its texture; recorded work retains this view.</summary>
public interface IGraphicsTextureView : IDisposable
{
    IGraphicsTexture Texture { get; }
}
