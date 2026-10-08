using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class TextureView : GpuResource, IGraphicsTextureView
{
    internal TextureView(Texture texture, A.TextureView native)
        : base(texture.Owner)
    {
        Texture = texture;
        Native = native;
        texture.Acquire();
    }

    IGraphicsTexture IGraphicsTextureView.Texture => Texture;

    internal A.TextureView Native { get; }

    internal Texture Texture { get; }

    protected override void ReleaseNative()
    {
        Native.Dispose();
        Texture.ReleaseLease();
    }
}
