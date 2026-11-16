using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuView(CpuGraphicsDevice owner, CpuTexture texture, TextureViewInfo info) : CpuResource(owner), IGraphicsTextureView
{
    public IGraphicsTexture Texture => texture;

    public TextureViewInfo Info => info;

    public override void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        texture.ReleaseView();
        base.Dispose();
    }
}
