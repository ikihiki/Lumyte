using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

internal sealed class DispatchTexture : IGraphicsTexture
{
    public uint Width { get; init; } = 16;

    public uint Height { get; init; } = 16;

    public uint MipLevels { get; init; } = 1;

    public uint ArrayLayers { get; init; } = 1;

    public TextureFormat Format { get; init; } = TextureFormat.Rgba8Srgb;

    public TextureUsage Usage { get; init; } = TextureUsage.CopySource | TextureUsage.CopyDestination | TextureUsage.RenderAttachment | TextureUsage.Sampled;

    internal List<string>? Lifetime { get; init; }

    internal bool Disposed { get; private set; }

    public (uint Width, uint Height) GetMipSize(uint mipLevel) => (Width >> (int)mipLevel, Height >> (int)mipLevel);

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null) => new DispatchTextureView(this, Lifetime);

    public void Dispose()
    {
        Disposed = true;
        Lifetime?.Add("texture");
    }
}
