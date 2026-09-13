using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Fixture = Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests.Fixture;
using static Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed partial class DirectX12NativeMeshTests
{
    [Fact]
    public void MeshAndVertexPipelinesShareRenderingAndReuseDepthStateVariants()
    {
        using var gpu = new Fixture();
        RequireMesh(gpu);
        Target color = gpu.Texture();
        Target depth = gpu.Texture(GpuFormat.D32Float);
        var meshDescription = new NativeGpuRasterPipelineDescription { Topology = null,
            MeshOutputTopology = NativeGpuMeshOutputTopology.Triangle, ColorTargets = [new(GpuFormat.Rgba8Unorm)],
            DepthStencilFormat = GpuFormat.D32Float };
        NativeGpuRasterPipelineHandle mesh = gpu.MeshPipeline(Mesh.Value, PixelShader.Value, description: meshDescription);
        NativeGpuRasterPipelineHandle vertex = gpu.Pipeline(meshDescription with
            { Topology = NativeGpuPrimitiveTopology.TriangleList, MeshOutputTopology = null });
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        commands.DiscardTexture(depth.View, GpuTextureLayout.DepthStencilWrite);
        commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear)],
            new(depth.RenderView, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store));
        var state = new NativeGpuDepthStencilState(DepthTest: true, DepthWrite: true, DepthCompare: GpuCompareOp.Less);
        commands.SetDepthStencilState(state);
        commands.SetPipeline(vertex);
        commands.Draw(Root(1, 0, 0, .75f), 3);
        commands.SetPipeline(mesh);
        commands.DispatchMesh(Root(0, 1, 0, .25f), 1);
        commands.SetDepthStencilState(state with { DepthCompare = GpuCompareOp.Never });
        commands.DispatchMesh(Root(1, 0, 0, .1f), 1);
        commands.SetDepthStencilState(state with { Front = state.Front with { Reference = 29 } });
        commands.SetViewport(new(0, 0, 8, 16));
        commands.SetScissor(new(0, 0, 8, 16));
        commands.DispatchMesh(Root(1, 1, 0, .1f), 1);
        commands.SetPipeline(vertex);
        commands.Draw(Root(0, 0, 1, .9f), 3);
        commands.EndRendering();
        NativeGpuLinearRegion pixels = gpu.Readback(commands, color);

        gpu.Submit(commands);

        Assert.Equal(new byte[] { 255, 255, 0, 255 }, Pixel(pixels, 4, 8));
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(pixels, 12, 8));
        Assert.Equal(2, gpu.Backend.RasterPipelineVariantCount(mesh));
        Assert.Equal(1, gpu.Backend.RasterPipelineVariantCount(vertex));
    }

    [Fact]
    public void MeshCanRenderDepthWithoutAPixelShader()
    {
        using var gpu = new Fixture();
        RequireMesh(gpu);
        Target depth = gpu.Texture(GpuFormat.D32Float);
        NativeGpuRasterPipelineHandle pipeline = gpu.MeshPipeline(Mesh.Value, null, description: new()
        { Topology = null, MeshOutputTopology = NativeGpuMeshOutputTopology.Triangle, DepthStencilFormat = GpuFormat.D32Float });
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(depth.View, GpuTextureLayout.DepthStencilWrite);
        commands.BeginRendering([], new(depth.RenderView, NativeGpuLoadOp.Clear, NativeGpuStoreOp.Store));
        commands.SetPipeline(pipeline);
        commands.SetDepthStencilState(new(DepthTest: true, DepthWrite: true));
        commands.DispatchMesh(Root(depth: .25f), 1);
        commands.EndRendering();
        NativeGpuLinearRegion readback = gpu.Readback(commands, depth, NativeGpuTextureAspect.Depth);

        gpu.Submit(commands);

        Assert.Equal(.25f, BitConverter.Int32BitsToSingle(Marshal.ReadInt32(readback.CpuAddress + 8 * 256 + 8 * 4)));
    }

    [Fact]
    public void MeshCanOutputLines()
    {
        using var gpu = new Fixture();
        RequireMesh(gpu);
        Target color = gpu.Texture();
        byte[] mesh = Compile(RootSource + """
            [outputtopology("line")][numthreads(1,1,1)]
            void meshMain(out vertices Output vertices[2],out indices uint2 indices[1]) {
                SetMeshOutputCounts(2,1);
                vertices[0].position=float4(-1,0,depth,1);vertices[0].color=float2(red,green);
                vertices[1].position=float4(1,0,depth,1);vertices[1].color=float2(red,green);
                indices[0]=uint2(0,1);
            }
            """, "ms_6_6", "meshMain");
        NativeGpuRasterPipelineHandle pipeline = gpu.MeshPipeline(mesh, PixelShader.Value, description: new()
            { Topology = null, MeshOutputTopology = NativeGpuMeshOutputTopology.Line, ColorTargets = [new(GpuFormat.Rgba8Unorm)] });
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear)]);
        commands.SetPipeline(pipeline);
        commands.DispatchMesh(Root(), 1);
        commands.EndRendering();
        NativeGpuLinearRegion readback = gpu.Readback(commands, color);

        gpu.Submit(commands);

        Assert.Contains(Enumerable.Range(0, 16).Select(y => Pixel(readback, 8, y)), pixel => pixel.SequenceEqual(new byte[] { 255, 0, 0, 255 }));
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(readback, 8, 0));
    }
}
