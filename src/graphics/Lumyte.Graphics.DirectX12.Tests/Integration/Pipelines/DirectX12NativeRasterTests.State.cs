using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed partial class DirectX12NativeRasterTests
{
    [Theory]
    [InlineData(NativeGpuFrontFace.Clockwise, true)]
    [InlineData(NativeGpuFrontFace.CounterClockwise, false)]
    public void CullingUsesThePostViewportFrontFace(NativeGpuFrontFace frontFace, bool visible)
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        byte[] vertex = Compile("""
            struct Output { float4 position:SV_Position; nointerpolation uint valid:TEXCOORD0; float red:TEXCOORD1; };
            Output vertexMain(uint vertex:SV_VertexID) {
                float2 positions[3]={float2(-.8,.8),float2(.8,.6),float2(-.4,-.8)};
                Output result; result.position=float4(positions[vertex],.5,1); result.valid=1; result.red=0; return result;
            }
            """, "vs_6_6", "vertexMain");
        NativeGpuRasterPipelineHandle pipeline = gpu.Pipeline(new() { ColorTargets = [new(GpuFormat.Rgba8Unorm)],
            FrontFace = frontFace, CullMode = NativeGpuCullMode.Back }, vertex);
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear, ClearColor: new(1, 0, 0, 1))]);
        commands.SetPipeline(pipeline);
        commands.Draw(Root(0, 1, 0), 3);
        commands.EndRendering();
        NativeGpuLinearRegion readback = gpu.Readback(commands, color);

        gpu.Submit(commands);

        Assert.Equal(visible ? new byte[] { 0, 255, 0, 255 } : new byte[] { 255, 0, 0, 255 }, Pixel(readback, 6, 6));
    }

    [Fact]
    public void DepthStateChangesResolveOnlyTheUsedPipelineVariants()
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        Target depth = gpu.Texture(GpuFormat.D32Float);
        NativeGpuRasterPipelineHandle pipeline = gpu.Pipeline(new()
            { ColorTargets = [new(GpuFormat.Rgba8Unorm)], DepthStencilFormat = GpuFormat.D32Float });
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        commands.DiscardTexture(depth.View, GpuTextureLayout.DepthStencilWrite);
        commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear)],
            new(depth.RenderView, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store, ClearDepth: 1));
        commands.SetPipeline(pipeline);
        var near = new NativeGpuDepthStencilState(DepthTest: true, DepthWrite: true, DepthCompare: GpuCompareOp.Less);
        commands.SetDepthStencilState(near);
        commands.Draw(Root(1, 0, 0, .25f), 3);
        commands.Draw(Root(0, 0, 1, .75f), 3);
        commands.SetDepthStencilState(near with { DepthWrite = false, DepthCompare = GpuCompareOp.Greater });
        commands.Draw(Root(0, 1, 0, .5f), 3);
        // This state is selected but no work uses it, so it must not allocate a PSO.
        commands.SetDepthStencilState(new(DepthTest: true, DepthCompare: GpuCompareOp.Never));
        commands.EndRendering();
        NativeGpuLinearRegion readback = gpu.Readback(commands, color);

        gpu.Submit(commands);

        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(readback, 8, 8));
        Assert.Equal(2, gpu.Backend.RasterPipelineVariantCount(pipeline));
    }

    [Fact]
    public void StencilReferencesRemainDynamicAcrossSubmittedPasses()
    {
        using var gpu = new Fixture();
        Target depth = gpu.Texture(GpuFormat.Depth24PlusStencil8);
        byte[] vertex = Compile("""
            float4 vertexMain(uint vertex:SV_VertexID):SV_Position {
                float2 positions[3]={float2(-1,1),float2(3,1),float2(-1,-3)};
                return float4(positions[vertex],.5,1);
            }
            """, "vs_6_6", "vertexMain");
        NativeGpuRasterPipelineHandle pipeline = gpu.Backend.CreateRasterPipeline(new()
            { DepthStencilFormat = GpuFormat.Depth24PlusStencil8, FrontFace = NativeGpuFrontFace.Clockwise },
            new(new NativeGpuShaderCode { Stage = GpuShaderStage.Vertex, Code = vertex, EntryPoint = "vertexMain" }));
        try
        {
            foreach (byte reference in new byte[] { 17, 43 })
            {
                using NativeGpuCommandBuffer commands = gpu.Commands();
                commands.DiscardTexture(depth.View, GpuTextureLayout.DepthStencilWrite);
                commands.BeginRendering([], new(depth.RenderView, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store,
                    NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store, ClearStencil: 0));
                commands.SetPipeline(pipeline);
                commands.SetDepthStencilState(new(StencilTest: true,
                    Front: new(PassOp: NativeGpuStencilOperation.Replace, Reference: reference),
                    Back: new(PassOp: NativeGpuStencilOperation.Replace, Reference: 99)));
                commands.Draw([], 3);
                commands.EndRendering();
                NativeGpuLinearRegion readback = gpu.Readback(commands, depth, NativeGpuTextureAspect.Stencil);

                gpu.Submit(commands);

                Assert.Equal(reference, Marshal.ReadByte(readback.CpuAddress + 8 * 256 + 8));
            }
            Assert.Equal(1, gpu.Backend.RasterPipelineVariantCount(pipeline));
        }
        finally { gpu.Backend.DestroyRasterPipeline(pipeline); }
    }

    [Fact]
    public void ReadOnlyDepthAndStencilPreserveEarlierContents()
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        Target depth = gpu.Texture(GpuFormat.Depth24PlusStencil8);
        NativeGpuRenderViewHandle readOnly = gpu.View(depth.View,
            NativeGpuRenderViewFlags.DepthReadOnly | NativeGpuRenderViewFlags.StencilReadOnly);
        NativeGpuRasterPipelineHandle pipeline = gpu.Pipeline(new()
            { ColorTargets = [new(GpuFormat.Rgba8Unorm)], DepthStencilFormat = GpuFormat.Depth24PlusStencil8 });
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(depth.View, GpuTextureLayout.DepthStencilWrite);
        commands.BeginRendering([], new(depth.RenderView, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store,
            NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store, ClearDepth: .25f, ClearStencil: 37));
        commands.EndRendering();
        commands.TextureTransition(depth.View, GpuTextureLayout.DepthStencilWrite, GpuTextureLayout.DepthStencilRead);
        commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear)], new(readOnly));
        commands.SetPipeline(pipeline);
        commands.SetDepthStencilState(new(DepthTest: true, DepthWrite: false, DepthCompare: GpuCompareOp.Greater,
            StencilTest: true, StencilWriteMask: 0,
            Front: new(Compare: GpuCompareOp.Equal, Reference: 37), Back: new(Compare: GpuCompareOp.Equal, Reference: 37)));
        commands.Draw(Root(0, 1, 0), 3);
        commands.EndRendering();
        NativeGpuLinearRegion pixels = gpu.Readback(commands, color);
        commands.TextureTransition(depth.View, GpuTextureLayout.DepthStencilRead, GpuTextureLayout.DepthStencilWrite);
        NativeGpuLinearRegion stencil = gpu.Readback(commands, depth, NativeGpuTextureAspect.Stencil);

        gpu.Submit(commands);

        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(pixels, 8, 8));
        Assert.Equal(37, Marshal.ReadByte(stencil.CpuAddress + 8 * 256 + 8));
    }
}
