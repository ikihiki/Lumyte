namespace Lumyte.Graphics.Portable.Tests.Views;

public sealed class GpuTextureViewTests
{
    [Fact]
    public void NormalizePreservesTheExternalTextureHandleAndOriginalValue()
    {
        var texture = new ExternalTexture();
        var original = new GpuTextureView(texture, new(BaseMip: 1));
        var description = new GpuTextureDescription(GpuTextureDimension.Texture2D, 64, 32, 1, 4, 1, 1,
            GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled);

        GpuTextureView normalized = original.Normalize(description);

        Assert.Same(texture, normalized.Texture);
        Assert.Equal(new GpuTextureViewDescription(GpuFormat.Rgba8Unorm, GpuTextureViewDimension.Texture2D,
            BaseMip: 1, MipCount: 3, LayerCount: 1), normalized.Description);
        Assert.Null(original.Description.MipCount);
    }

    private sealed class ExternalTexture : GpuTextureHandle;
}
