namespace Lumyte.Graphics.Native.Tests.Commands;

public sealed class NativeGpuDepthStencilAttachmentTests
{
    [Theory]
    [InlineData(NativeGpuRenderViewFlags.None, false, false)]
    [InlineData(NativeGpuRenderViewFlags.DepthReadOnly, true, false)]
    [InlineData(NativeGpuRenderViewFlags.StencilReadOnly, false, true)]
    [InlineData(NativeGpuRenderViewFlags.DepthReadOnly | NativeGpuRenderViewFlags.StencilReadOnly, true, true)]
    public void ReadOnlyConditionsDeriveFromTheBorrowedView(NativeGpuRenderViewFlags flags, bool depth, bool stencil)
    {
        var attachment = new NativeGpuDepthStencilAttachment(new ExternalRenderView(flags));

        Assert.Equal((depth, stencil), (attachment.DepthReadOnly, attachment.StencilReadOnly));
    }

    [Fact]
    public void WritableStencilLeavesReadOnlyDepthOperationsUnspecified()
    {
        var view = new ExternalRenderView(NativeGpuRenderViewFlags.DepthReadOnly);

        var attachment = new NativeGpuDepthStencilAttachment(view,
            StencilLoadOp: NativeGpuLoadOp.Clear, StencilStoreOp: NativeGpuStoreOp.Store, ClearStencil: 71);

        Assert.Equal((null, null, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store, (byte)71),
            (attachment.DepthLoadOp, attachment.DepthStoreOp,
                attachment.StencilLoadOp, attachment.StencilStoreOp, attachment.ClearStencil));
    }

    private sealed class ExternalRenderView(NativeGpuRenderViewFlags flags) : NativeGpuRenderViewHandle(flags);
}
