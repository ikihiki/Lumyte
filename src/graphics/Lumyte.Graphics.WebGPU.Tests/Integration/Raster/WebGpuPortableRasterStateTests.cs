using P = Lumyte.Graphics.Portable;
using Fixture = Lumyte.Graphics.WebGPU.Tests.WebGpuPortableRasterFixture;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
[Trait("Category", "WebGpuPortableConformance")]
public sealed class WebGpuPortableRasterStateTests
{
    [Fact]
    public async Task AVertexOnlyPassStoresDepthForAFollowingColorPass()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuTextureHandle depth = fixture.Texture(format: GpuFormat.D32Float, usage: P.GpuTextureUsage.DepthStencilAttachment);
        P.GpuShaderModuleHandle module = fixture.Module(Fixture.RootShader);
        var state = new P.GpuDepthStencilState(DepthTest: true, DepthWrite: true, DepthCompare: GpuCompareOp.Less);
        P.GpuRasterPipelineHandle depthOnly = fixture.Pipeline(
            new P.GpuRasterPipelineDescription([], GpuFormat.D32Float) { DepthStencil = state },
            new P.GpuShaderProgramDescription([new(module, P.GpuShaderStage.Vertex, "vertex")], [], 8));
        P.GpuRasterPipelineHandle color = fixture.Pipeline(Fixture.RootShader,
            new P.GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)], GpuFormat.D32Float) { DepthStencil = state }, 8);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([], new(new(depth), DepthLoadOperation: P.GpuAttachmentLoadOperation.Clear,
            DepthStoreOperation: P.GpuAttachmentStoreOperation.Store, ClearValue: new(1, 0)));
        commands.SetPipeline(depthOnly);
        commands.SetRootData(new Fixture.Root(0, 0.25f));
        commands.Draw(3);
        commands.EndRendering();

        commands.BeginRendering([Fixture.Color(target)], new(new(depth), DepthLoadOperation: P.GpuAttachmentLoadOperation.Load,
            DepthStoreOperation: P.GpuAttachmentStoreOperation.Store));
        commands.SetPipeline(color);
        commands.SetRootData(new Fixture.Root(0xff0000ff, 0.75f));
        commands.Draw(3);
        commands.EndRendering();
        P.GpuBufferHandle readback = fixture.Readback(commands, target);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.Transparent, await fixture.PixelAsync(readback));
    }

    [Fact]
    public async Task DepthTestingKeepsTheNearestDraw()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuTextureHandle depth = fixture.Texture(format: GpuFormat.D32Float, usage: P.GpuTextureUsage.DepthStencilAttachment);
        var description = new P.GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)], GpuFormat.D32Float)
        {
            DepthStencil = new(DepthTest: true, DepthWrite: true, DepthCompare: GpuCompareOp.Less),
        };
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(Fixture.RootShader, description, 8);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([Fixture.Color(target)], new(new(depth),
            DepthLoadOperation: P.GpuAttachmentLoadOperation.Clear,
            DepthStoreOperation: P.GpuAttachmentStoreOperation.Store, ClearValue: new(1, 0)));
        commands.SetPipeline(pipeline);
        commands.SetRootData(new Fixture.Root(0xff0000ff, 0.25f));
        commands.Draw(3);

        commands.SetRootData(new Fixture.Root(0xff00ff00, 0.75f));
        commands.Draw(3);
        commands.EndRendering();
        P.GpuBufferHandle readback = fixture.Readback(commands, target);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.RedColor, await fixture.PixelAsync(readback));
    }

    [Fact]
    public async Task StencilReferenceAndFaceOperationsMaskTheFollowingDraw()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture();
        P.GpuTextureHandle depth = fixture.Texture(format: GpuFormat.Depth24PlusStencil8,
            usage: P.GpuTextureUsage.DepthStencilAttachment);
        var replace = new P.GpuStencilFaceState(PassOp: P.GpuStencilOp.Replace);
        var equals = new P.GpuStencilFaceState(Compare: GpuCompareOp.Equal);
        P.GpuRasterPipelineHandle mask = fixture.Pipeline(description: new P.GpuRasterPipelineDescription(
            [new(GpuFormat.Rgba8Unorm, P.GpuColorWriteMask.None)], GpuFormat.Depth24PlusStencil8)
        {
            DepthStencil = new(StencilTest: true, Front: replace, Back: replace),
        });
        P.GpuRasterPipelineHandle draw = fixture.Pipeline(description: new P.GpuRasterPipelineDescription(
            [new(GpuFormat.Rgba8Unorm)], GpuFormat.Depth24PlusStencil8)
        {
            DepthStencil = new(StencilTest: true, StencilWriteMask: 0, Front: equals, Back: equals),
        });
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([Fixture.Color(target)], new(new(depth),
            DepthLoadOperation: P.GpuAttachmentLoadOperation.Clear,
            DepthStoreOperation: P.GpuAttachmentStoreOperation.Store,
            StencilLoadOperation: P.GpuAttachmentLoadOperation.Clear,
            StencilStoreOperation: P.GpuAttachmentStoreOperation.Store, ClearValue: new(1, 0)));
        commands.SetPipeline(mask);
        commands.SetStencilReference(3);
        commands.SetViewportAndScissor(new(0, 0, 4, 4), new(0, 0, 2, 4));
        commands.Draw(3);

        commands.SetPipeline(draw);
        commands.SetViewportAndScissor(new(0, 0, 4, 4), new(0, 0, 4, 4));
        commands.Draw(3);
        commands.EndRendering();
        P.GpuBufferHandle readback = fixture.Readback(commands, target);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(Fixture.Pixel.RedColor, await fixture.PixelAsync(readback, 0, 1));
        Assert.Equal(Fixture.Pixel.Transparent, await fixture.PixelAsync(readback, 3, 1));
    }

    [Fact]
    public async Task BlendConstantsAreAppliedToTheSelectedColorTarget()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        P.GpuTextureHandle target = fixture.Texture();
        var blend = new P.GpuBlendDescription(SourceColorFactor: P.GpuBlendFactor.Constant,
            DestinationColorFactor: P.GpuBlendFactor.OneMinusConstant);
        var description = new P.GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm, Blend: blend)]);
        P.GpuRasterPipelineHandle pipeline = fixture.Pipeline(description: description);
        P.GpuCommandBuffer commands = fixture.Record();
        commands.BeginRendering([Fixture.Color(target, new(0, 0, 1, 1))]);
        commands.SetPipeline(pipeline);

        commands.SetBlendConstant(new(1, 0, 0, 1));
        commands.Draw(3);
        commands.EndRendering();
        P.GpuBufferHandle readback = fixture.Readback(commands, target);
        await fixture.SubmitAndWaitAsync(commands);

        Assert.Equal(new Fixture.Pixel(255, 0, 255, 255), await fixture.PixelAsync(readback));
    }
}
