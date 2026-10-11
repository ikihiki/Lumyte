using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class TestTexture : IGraphicsTexture
{
    public uint Width => 16;

    public uint Height => 16;

    public uint MipLevels => 3;

    public uint ArrayLayers => 2;

    public TextureFormat Format { get; init; } = TextureFormat.Rgba8Unorm;

    public TextureUsage Usage { get; init; } = TextureUsage.CopySource | TextureUsage.CopyDestination | TextureUsage.RenderAttachment | TextureUsage.Sampled;

    internal bool Disposed { get; private set; }

    public (uint Width, uint Height) GetMipSize(uint mipLevel) => (Width >> (int)mipLevel, Height >> (int)mipLevel);

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null) => throw new NotSupportedException();

    public void Dispose() => Disposed = true;
}
