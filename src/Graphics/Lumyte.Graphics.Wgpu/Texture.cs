using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class Texture : GpuResource, IGraphicsTexture
{
    internal Texture(WgpuDevice owner, A.Texture native, TextureDesc desc)
        : base(owner)
    {
        (Native, Width, Height, Usage, Format) = (native, desc.Width, desc.Height, desc.Usage, desc.Format);
    }

    public uint Width { get; }

    public uint Height { get; }

    public TextureUsage Usage { get; }

    public TextureFormat Format { get; }

    internal A.Texture Native { get; }

    public IGraphicsTextureView CreateView()
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            return new TextureView(this, Owner.Validated(Native.CreateView()));
        }
    }

    protected override void ReleaseNative() => Native.Dispose();
}
