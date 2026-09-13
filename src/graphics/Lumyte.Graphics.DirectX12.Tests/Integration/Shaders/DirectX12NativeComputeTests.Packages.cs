using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Lumyte.Graphics.Native.Shaders;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed partial class DirectX12NativeComputeTests
{
    [Fact]
    public void LoadedPackageCanBeDisposedBeforeItsPipelineIsSubmitted()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using var output = new Region(backend, NativeGpuMemoryKind.GpuOnly);
        using var readback = new Region(backend, NativeGpuMemoryKind.Readback);
        using var descriptors = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Resource, 8);
        using var samplers = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Sampler, 1);
        byte[] callerCode = RootShader.Value.ToArray();
        var artifact = new NativeShaderArtifact(NativeShaderTarget.DirectX12, GpuShaderCodeFormat.Dxil,
            [new NativeShaderStageArtifact(GpuShaderStage.Compute, "computeMain", callerCode)],
            NativeShaderCapabilities.BufferDescriptors, NativeShaderDescriptorHeapAbi.DirectX12,
            new NativeShaderInputLayout("DirectX12Root", 256, 16, [
                new("outputIndex", NativeShaderInputFieldKind.DescriptorIndex, 0, 4),
                new("outputOffset", NativeShaderInputFieldKind.Scalar, 4, 4),
                new("value", NativeShaderInputFieldKind.Scalar, 8, 4),
                new("tail", NativeShaderInputFieldKind.Scalar, 252, 4)
            ]), [], "directx12-package-compute-root-v1");
        var package = new NativeShaderPackage(NativeShaderPackage.CurrentVersion, [artifact]);
        Array.Clear(callerCode);
        using NativeShaderProgram program = new NativeShaderLoader(backend).Load(package);
        NativeGpuComputePipelineHandle pipeline = backend.CreateComputePipeline(program.Code);
        program.Dispose();
        try
        {
            backend.WriteBufferDescriptor(descriptors.Value, 5, new(output.Value, 512, 256), NativeGpuBufferAccess.ReadWrite);
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.SetComputePipeline(pipeline);
            commands.SetResourceDescriptorHeap(descriptors.Value);
            commands.SetSamplerDescriptorHeap(samplers.Value);
            uint[] root = new uint[64];
            root[0] = 5; root[2] = 42; root[63] = 1000;
            commands.Dispatch(MemoryMarshal.AsBytes(root.AsSpan()), 3);
            ReadOutput(commands, output, readback, 512, 12);

            Submit(backend, commands);

            Assert.Equal(new uint[] { 1042, 1043, 1044 }, ReadWords(readback.Value.CpuAddress, 3));
        }
        finally { backend.DestroyComputePipeline(pipeline); }
    }
}
