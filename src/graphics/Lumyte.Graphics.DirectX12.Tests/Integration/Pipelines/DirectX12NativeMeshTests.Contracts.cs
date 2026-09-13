using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Fixture = Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests.Fixture;
using static Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed partial class DirectX12NativeMeshTests
{
    [Fact]
    public void AnUnusedInvalidMeshShaderDoesNotCreateAPso()
    {
        using var gpu = new Fixture();
        RequireMesh(gpu);
        NativeGpuRasterPipelineHandle pipeline = gpu.MeshPipeline([1, 2, 3, 4], PixelShader.Value);
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.SetPipeline(pipeline);

        gpu.Submit(commands);

        Assert.Equal(0, gpu.Backend.RasterPipelineVariantCount(pipeline));
    }

    [Fact]
    public void MeshPsoFailureRejectsTheWholeBatchBeforeExecution()
    {
        using var gpu = new Fixture();
        RequireMesh(gpu);
        Target color = gpu.Texture();
        NativeGpuLinearRegion upload = gpu.Region(NativeGpuMemoryKind.CpuVisible);
        NativeGpuLinearRegion readback = gpu.Region(NativeGpuMemoryKind.Readback);
        Marshal.WriteInt32(upload.CpuAddress, 11);
        Marshal.WriteInt32(readback.CpuAddress, 97);
        NativeGpuRasterPipelineHandle pipeline = gpu.MeshPipeline([1, 2, 3, 4], PixelShader.Value);
        using NativeGpuCommandBuffer first = gpu.Commands();
        using NativeGpuCommandBuffer second = gpu.Commands();
        using NativeGpuSemaphore semaphore = gpu.Backend.MainQueue.CreateSemaphore(0);
        first.CopyMemory(new(upload, 0, 4), new(readback, 0, 4));
        second.DiscardTexture(color.View, GpuTextureLayout.ColorAttachment);
        second.BeginRendering([new(color.RenderView, NativeGpuLoadOp.Clear)]);
        second.SetPipeline(pipeline);
        second.DispatchMesh([], 1);
        second.EndRendering();

        NativeGpuException error = Assert.Throws<NativeGpuException>(() => gpu.Backend.MainQueue.Submit([first, second], semaphore, 1));
        using NativeGpuCommandBuffer drain = gpu.Commands();
        gpu.Submit(drain);

        Assert.Contains("CreatePipelineState(Native mesh)", error.Message);
        Assert.Equal(97, Marshal.ReadInt32(readback.CpuAddress));
        Assert.Equal(0, gpu.Backend.RasterPipelineVariantCount(pipeline));
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void MeshRootBytesCannotBeTruncatedToWholeConstants(bool indirect, int size)
    {
        using var gpu = new Fixture();
        RequireMesh(gpu);
        Target color = gpu.Texture();
        NativeGpuLinearRegion arguments = gpu.Region(NativeGpuMemoryKind.GpuOnly);
        NativeGpuRasterPipelineHandle pipeline = gpu.MeshPipeline(Mesh.Value, PixelShader.Value);
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.BeginRendering([new(color.RenderView)]);
        commands.SetPipeline(pipeline);

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
        {
            if (indirect) { commands.DispatchMeshIndirect(new byte[size], new(arguments, 128, 12)); }
            else { commands.DispatchMesh(new byte[size], 1); }
        });

        Assert.Equal("rootData", error.ParamName);
        commands.EndRendering();
    }

    [Fact]
    public void MeshIndirectRangeMustContainOneCompleteRecord()
    {
        using var gpu = new Fixture();
        RequireMesh(gpu);
        Target color = gpu.Texture();
        NativeGpuLinearRegion arguments = gpu.Region(NativeGpuMemoryKind.GpuOnly);
        NativeGpuRasterPipelineHandle pipeline = gpu.MeshPipeline(Mesh.Value, PixelShader.Value);
        using NativeGpuCommandBuffer commands = gpu.Commands();
        commands.BeginRendering([new(color.RenderView)]);
        commands.SetPipeline(pipeline);

        ArgumentException error = Assert.Throws<ArgumentException>(() => commands.DispatchMeshIndirect([], new(arguments, 128, 8)));

        Assert.Equal("arguments", error.ParamName);
        commands.EndRendering();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshWorkRequiresAnActiveRenderingSection(bool indirect)
    {
        using var gpu = new Fixture();
        RequireMesh(gpu);
        NativeGpuLinearRegion arguments = gpu.Region(NativeGpuMemoryKind.GpuOnly);
        using NativeGpuCommandBuffer commands = gpu.Commands();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
        {
            if (indirect) { commands.DispatchMeshIndirect([], new(arguments, 0, 12)); }
            else { commands.DispatchMesh([], 1); }
        });

        Assert.Contains("active rendering", error.Message);
    }

    [Fact]
    public void DestroyedMeshPipelineCannotBeSelectedAgain()
    {
        using var gpu = new Fixture();
        RequireMesh(gpu);
        NativeGpuRasterPipelineHandle pipeline = gpu.Backend.CreateRasterPipeline(new()
            { Topology = null, MeshOutputTopology = NativeGpuMeshOutputTopology.Triangle },
            new(new NativeGpuShaderCode { Stage = GpuShaderStage.Mesh, Code = Mesh.Value }));
        gpu.Backend.DestroyRasterPipeline(pipeline);
        using NativeGpuCommandBuffer commands = gpu.Commands();

        Assert.Throws<ObjectDisposedException>(() => commands.SetPipeline(pipeline));
    }
}
