namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class TestTexture : IGraphicsTexture
{
    public uint Width { get; init; } = 16;

    public uint Height { get; init; } = 16;

    public uint MipLevels { get; init; } = 3;

    public uint ArrayLayers { get; init; } = 2;

    public TextureFormat Format { get; init; } = TextureFormat.Rgba8Unorm;

    public TextureUsage Usage { get; init; } = TextureUsage.CopySource | TextureUsage.CopyDestination | TextureUsage.RenderAttachment | TextureUsage.Sampled;

    internal List<string>? Lifetime { get; init; }

    internal bool Disposed { get; private set; }

    public (uint Width, uint Height) GetMipSize(uint mipLevel) => (Width >> (int)mipLevel, Height >> (int)mipLevel);

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null) => new TestTextureView(this, Lifetime);

    public void Dispose()
    {
        Disposed = true;
        Lifetime?.Add("texture");
    }
}
