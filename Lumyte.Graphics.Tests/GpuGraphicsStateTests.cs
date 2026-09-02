using Lumyte.Graphics;

namespace Lumyte.Graphics.Tests;

public sealed class GpuGraphicsStateTests
{
    [Fact]
    public void NormalizeCanonicalizesDisabledStencilState()
    {
        GpuDepthStencilState value = GpuDepthStencilState.Default with
        {
            StencilFront = new(GpuCompareOp.Less, GpuStencilOp.Replace, GpuStencilOp.Invert, GpuStencilOp.Zero),
            StencilReadMask = 3,
            StencilWriteMask = 7,
        };

        GpuDepthStencilState normalized = value.Normalize();

        Assert.Equal(GpuStencilFaceState.Default, normalized.StencilFront);
        Assert.Equal(0xffu, normalized.StencilReadMask);
        Assert.Equal(0xffu, normalized.StencilWriteMask);
    }

    [Fact]
    public void NormalizeRejectsStencilMasksLargerThanOneByte()
    {
        GpuDepthStencilState value = GpuDepthStencilState.Default with { StencilReadMask = 256 };

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => value.Normalize());

        Assert.Equal("StencilReadMask", exception.ParamName);
    }

    [Fact]
    public void DepthStateRequiresDepthAttachment()
    {
        var attachments = new GpuAttachmentLayout(GpuFormat.Rgba8Unorm);
        GpuDepthStencilState state = GpuDepthStencilState.Default with { DepthTest = true };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => GpuGraphicsStateValidation.ValidateDepthStencilAttachmentRequirements(attachments, state));

        Assert.Contains("depth attachment", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StencilStateAcceptsCombinedDepthStencilAttachment()
    {
        var attachments = new GpuAttachmentLayout(GpuFormat.Rgba8Unorm, GpuFormat.Depth24PlusStencil8);
        GpuDepthStencilState state = GpuDepthStencilState.Default with { StencilTest = true };

        GpuGraphicsStateValidation.ValidateDepthStencilAttachmentRequirements(attachments, state);
    }
}
