using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12TextureView(Direct3D12Texture texture, TextureViewInfo info) : IGraphicsTextureView
{
    private bool _disposed;

    public IGraphicsTexture Texture => texture;

    public TextureViewInfo Info { get; } = info;

    internal Direct3D12Device Owner => texture.Owner;

    internal nint Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return texture.Native;
        }
    }

    public void Dispose() => _disposed = true;
}
