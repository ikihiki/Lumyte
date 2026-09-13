using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Fixture = Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests.Fixture;
using static Lumyte.Graphics.DirectX12.Tests.DirectX12NativeRasterTests;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed partial class DirectX12NativeMeshTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MeshDispatchUsesAllThreeDimensionsAndOneGpuWrittenIndirectRecord(bool amplification, bool indirect)
    {
        using var gpu = new Fixture();
        RequireMesh(gpu);
        Target target = gpu.Texture();
        NativeGpuLinearRegion arguments = gpu.Region(NativeGpuMemoryKind.GpuOnly);
        NativeGpuLinearRegion output = gpu.Region(NativeGpuMemoryKind.GpuOnly);
        NativeGpuLinearRegion readback = gpu.Region(NativeGpuMemoryKind.Readback);
        gpu.WriteBuffer(2, new(arguments, 256, 256), NativeGpuBufferAccess.ReadWrite);
        gpu.WriteBuffer(3, new(output, 512, 256), NativeGpuBufferAccess.ReadWrite);
        const string storeGroup = """
            RWByteAddressBuffer output=ResourceDescriptorHeap[descriptor];
            uint index=group.x+group.y*2+group.z*6;
            output.Store(index*4,41+index);
            """;
        byte[] mesh = Compile(MeshSource(amplification, amplification ? "" : storeGroup), "ms_6_6", "meshMain");
        byte[]? amplifying = amplification ? Compile(AmplificationSource(storeGroup), "as_6_6", "amplificationMain") : null;
        NativeGpuRasterPipelineHandle pipeline = gpu.MeshPipeline(mesh, PixelShader.Value, amplifying);
        byte[] compute = DirectX12NativeComputeTests.Compile("""
            cbuffer Root:register(b0) {uint descriptor;};
            [numthreads(1,1,1)] void computeMain() {
                RWByteAddressBuffer arguments=ResourceDescriptorHeap[descriptor];
                arguments.Store3(128,uint3(2,3,2));
            }
            """);
        NativeGpuComputePipelineHandle writer = gpu.Backend.CreateComputePipeline(new(new NativeGpuShaderCode
            { Stage = GpuShaderStage.Compute, Code = compute }));
        try
        {
            using NativeGpuCommandBuffer commands = gpu.Commands();
            if (indirect)
            {
                commands.SetComputePipeline(writer);
                commands.Dispatch(BitConverter.GetBytes(2u), 1);
                commands.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.DrawIndirect, GpuAccess.IndirectRead);
            }
            commands.DiscardTexture(target.View, GpuTextureLayout.ColorAttachment);
            commands.BeginRendering([new(target.RenderView, NativeGpuLoadOp.Clear)]);
            commands.SetPipeline(pipeline);
            byte[] root = Root(0, 1, 0, descriptor: 3);
            if (indirect) { commands.DispatchMeshIndirect(root, new(arguments, 384, 12)); }
            else { commands.DispatchMesh(root, 2, 3, 2); }
            Array.Clear(root);
            commands.EndRendering();
            commands.Barrier(amplification ? GpuStage.AmplificationShader : GpuStage.MeshShader,
                GpuAccess.ShaderWrite, GpuStage.Copy, GpuAccess.CopyRead);
            commands.CopyMemory(new(output, 512, 48), new(readback, 0, 48));
            commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
            NativeGpuLinearRegion pixels = gpu.Readback(commands, target);

            gpu.Submit(commands);

            var actual = new int[12];
            Marshal.Copy(readback.CpuAddress, actual, 0, actual.Length);
            Assert.Equal(Enumerable.Range(41, 12), actual);
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(pixels, 8, 8));
        }
        finally { gpu.Backend.DestroyComputePipeline(writer); }
    }
}
