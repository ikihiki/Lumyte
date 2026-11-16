using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Attachment loads, not render-target capability, determine read visibility.</summary>
public sealed class DepthLoadReviewTests
{
    /// <summary>Write-only visibility is sufficient exactly when no existing aspect is loaded.</summary>
    /// <param name="format">The attachment format.</param>
    /// <param name="depth">The depth load operation.</param>
    /// <param name="stencil">The stencil load operation.</param>
    /// <param name="reads">Whether the pass needs read visibility.</param>
    [Theory]
    [InlineData(TextureFormat.Depth32Float, AttachmentLoadOp.Clear, AttachmentLoadOp.Clear, false)]
    [InlineData(TextureFormat.Depth32Float, AttachmentLoadOp.Clear, AttachmentLoadOp.Load, false)]
    [InlineData(TextureFormat.Depth32Float, AttachmentLoadOp.Load, AttachmentLoadOp.Clear, true)]
    [InlineData(TextureFormat.Depth24Stencil8, AttachmentLoadOp.Clear, AttachmentLoadOp.Clear, false)]
    [InlineData(TextureFormat.Depth24Stencil8, AttachmentLoadOp.Load, AttachmentLoadOp.Clear, true)]
    [InlineData(TextureFormat.Depth24Stencil8, AttachmentLoadOp.Clear, AttachmentLoadOp.Load, true)]
    public void LoadOperationsDetermineReadAccess(TextureFormat format, AttachmentLoadOp depth, AttachmentLoadOp stencil, bool reads)
    {
        using var device = new ValidationDevice(new FactoryBackend());
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 8, Height = 8, MipLevels = 2, Format = format, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsCommandBuffer command = EncoderExecutionReviewTests.Command(device, new());
        var range = new TextureSubresourceRange(1, 1, 0, 1);
        var write = new BarrierScope(PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite);
        var readWrite = new BarrierScope(PipelineStage.DepthStencil, ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite);
        command.Barrier(new TextureBarrierDesc { Texture = texture, Range = range, BeforeState = TextureState.Undefined, AfterState = TextureState.DepthStencilAttachment, Before = default, After = write });
        var pass = new RenderPassDesc { DepthStencilAttachment = new() { View = view, DepthLoadOp = depth, StencilLoadOp = stencil } };
        if (reads)
        {
            Assert.Throws<InvalidOperationException>(() => command.BeginRenderPass(pass));
            command.Barrier(new TextureBarrierDesc { Texture = texture, Range = range, BeforeState = TextureState.DepthStencilAttachment, AfterState = TextureState.DepthStencilAttachment, Before = write, After = readWrite });
        }

        IRenderEncoder render = command.BeginRenderPass(pass);
        render.End();
    }
}
