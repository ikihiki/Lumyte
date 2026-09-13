using Lumyte.Graphics.Native;
using Lumyte.Graphics.Native.Shaders;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed partial class DirectX12NativeRasterTests
{
    [Fact]
    public void RasterPipelineUsesLoadedProgramAfterItsOwnerIsDisposed()
    {
        using var gpu = new Fixture();
        Target color = gpu.Texture();
        var package = new NativeShaderPackage(NativeShaderPackage.CurrentVersion, [
            new NativeShaderArtifact(NativeShaderTarget.DirectX12, GpuShaderCodeFormat.Dxil, [
                new NativeShaderStageArtifact(GpuShaderStage.Vertex, "vertexMain", VertexCode.Value),
                new NativeShaderStageArtifact(GpuShaderStage.Pixel, "pixelMain", PixelCode.Value)
            ], NativeShaderCapabilities.None, NativeShaderDescriptorHeapAbi.None,
            new NativeShaderInputLayout("DirectX12RasterRoot", 256, 16, [
                new("depth", NativeShaderInputFieldKind.Scalar, 0, 4),
                new("vertexBase", NativeShaderInputFieldKind.Scalar, 4, 4),
                new("expectedInstance", NativeShaderInputFieldKind.Scalar, 8, 4),
                new("green", NativeShaderInputFieldKind.Scalar, 16, 4),
                new("blue", NativeShaderInputFieldKind.Scalar, 20, 4),
                new("red", NativeShaderInputFieldKind.Scalar, 252, 4)
            ]), [], "directx12-package-raster-root-v1")
        ]);
        using NativeShaderProgram program = new NativeShaderLoader(gpu.Backend).Load(package);
        NativeGpuRasterPipelineHandle pipeline = gpu.Backend.CreateRasterPipeline(
            new() { ColorTargets = [new(GpuFormat.Rgba8Unorm)] }, program.Code);
        program.Dispose();
        try
        {
            using NativeGpuCommandBuffer commands = gpu.Commands();
            commands.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
            commands.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear)]);
            commands.SetPipeline(pipeline);
            commands.Draw(Root(0, 1, 0), 3);
            commands.EndRendering();
            NativeGpuLinearRegion readback = gpu.Readback(commands, color);

            gpu.Submit(commands);

            Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(readback, 8, 8));
        }
        finally { gpu.Backend.DestroyRasterPipeline(pipeline); }
    }
}
