namespace Lumyte.Graphics.Portable.Tests.Views;

public sealed class GpuAttachmentTests
{
    [Fact]
    public void DepthAndStencilOperationsCanBeSelectedIndependently()
    {
        var view = new GpuTextureView(new ExternalTexture());
        var attachment = new GpuDepthStencilAttachment(view,
            DepthReadOnly: true,
            StencilLoadOperation: GpuAttachmentLoadOperation.Clear,
            StencilStoreOperation: GpuAttachmentStoreOperation.Store,
            ClearValue: new(1, 257));

        var actual = (attachment.DepthReadOnly, attachment.DepthLoadOperation, attachment.DepthStoreOperation,
            attachment.StencilLoadOperation, attachment.StencilStoreOperation, attachment.ClearValue);

        Assert.Equal((true, (GpuAttachmentLoadOperation?)null, (GpuAttachmentStoreOperation?)null,
            (GpuAttachmentLoadOperation?)GpuAttachmentLoadOperation.Clear,
            (GpuAttachmentStoreOperation?)GpuAttachmentStoreOperation.Store, new GpuClearDepthStencil(1, 257)), actual);
    }

    [Fact]
    public void ColorAttachmentsRetainTheNonOwningViewAndClearPrecision()
    {
        var view = new GpuTextureView(new ExternalTexture(), new(BaseMip: 1, MipCount: 1));
        var clear = new GpuClearColor(1.0000000001, 0, 0, 1);

        var attachment = new GpuColorAttachment(view, GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Discard, clear);

        Assert.Equal((view, clear), (attachment.View, attachment.ClearColor));
    }

    private sealed class ExternalTexture : GpuTextureHandle;
}
