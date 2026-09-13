using Lumyte.Graphics.Native;
using Lumyte.Graphics.Native.Shaders;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe partial class VulkanNativeComputeTests
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void LoadedRawPackageCanBeDisposedBeforeItsPipelineIsSubmitted()
    {
        using var resources = new Resources();
        byte[] callerCode = ShaderBytes("NativeRawCompute.spv");
        var package = new NativeShaderPackage(NativeShaderPackage.CurrentVersion, [
            new NativeShaderArtifact(NativeShaderTarget.Vulkan, GpuShaderCodeFormat.SpirV,
                [new NativeShaderStageArtifact(GpuShaderStage.Compute, "rawMain", callerCode)],
                NativeShaderCapabilities.RawShaderPointers, NativeShaderDescriptorHeapAbi.None,
                PackageRootLayout(), [], "vulkan-package-raw-root-v1")
        ]);
        Array.Clear(callerCode);
        using NativeShaderProgram program = new NativeShaderLoader(resources.Backend).Load(package);
        NativeGpuComputePipelineHandle pipeline = resources.Backend.CreateComputePipeline(program.Code);
        program.Dispose();
        try
        {
            NativeGpuLinearRegion output = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
            NativeGpuLinearRegion readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
            var queue = resources.Backend.MainQueue;
            using NativeGpuSemaphore completion = resources.Backend.CreateSemaphore(0);
            using NativeGpuCommandBuffer commands = queue.StartCommandRecording();
            commands.SetComputePipeline(pipeline);
            commands.Dispatch(Root(new NativeGpuRange(output, 64, 64).GpuAddress, 42, tail: 1000), 3);
            Readback(commands, new(output, 64, 12), new(readback, 0, 12));

            queue.Submit([commands], new(completion, 1));
            completion.WaitCpu(1);

            Assert.Equal(new uint[] { 1042, 1043, 1044 }, Words(readback)[..3].ToArray());
        }
        finally { resources.Backend.DestroyComputePipeline(pipeline); }
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void LoadedUnifiedHeapPackageUsesTheDevicesDescriptorLayout()
    {
        using var resources = new Resources();
        var package = new NativeShaderPackage(NativeShaderPackage.CurrentVersion, [
            new NativeShaderArtifact(NativeShaderTarget.Vulkan, GpuShaderCodeFormat.SpirV,
                [new NativeShaderStageArtifact(GpuShaderStage.Compute, "heapMain", ShaderBytes("NativeHeapCompute.spv"))],
                NativeShaderCapabilities.BufferDescriptors, NativeShaderDescriptorHeapAbi.VulkanUnified,
                PackageRootLayout(), [], "vulkan-package-unified-heap-v1")
        ]);
        using NativeShaderProgram program = new NativeShaderLoader(resources.Backend).Load(package);
        NativeGpuComputePipelineHandle pipeline = resources.Backend.CreateComputePipeline(program.Code);
        program.Dispose();
        try
        {
            NativeGpuLinearRegion output = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
            NativeGpuLinearRegion readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
            NativeGpuLinearRegion upload = resources.Linear(256, NativeGpuMemoryKind.CpuVisible);
            Bytes(upload)[0] = 63; Bytes(upload)[1] = 127; Bytes(upload)[2] = 211; Bytes(upload)[3] = 255;
            NativeGpuTextureHandle texture = resources.Texture();
            NativeGpuDescriptorHeap resourceHeap = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 8);
            NativeGpuDescriptorHeap samplerHeap = resources.Descriptors(NativeGpuDescriptorHeapKind.Sampler, 8);
            resources.Backend.WriteBufferDescriptor(resourceHeap, 3, new(output, 64, 64), NativeGpuBufferAccess.ReadWrite);
            resources.Backend.WriteTextureDescriptor(resourceHeap, 5, View(texture));
            resources.Backend.WriteSamplerDescriptor(samplerHeap, 2, new());
            var queue = resources.Backend.MainQueue;
            using NativeGpuSemaphore completion = resources.Backend.CreateSemaphore(0);
            using NativeGpuCommandBuffer commands = queue.StartCommandRecording();
            commands.SetComputePipeline(pipeline);
            commands.SetResourceDescriptorHeap(resourceHeap);
            commands.SetSamplerDescriptorHeap(samplerHeap);
            commands.CopyMemoryToTexture(new(upload, 0, 4), texture,
                new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(1, 1, 1), 4, 4));
            commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.ComputeShader, GpuAccess.ShaderRead);
            commands.Dispatch(Root(0, 7, buffer: 3, texture: 5, sampler: 2, tail: 800), 3);
            Readback(commands, new(output, 64, 12), new(readback, 0, 12));

            queue.Submit([commands], new(completion, 1));
            completion.WaitCpu(1);

            Assert.Equal(new uint[] { 870, 871, 872 }, Words(readback)[..3].ToArray());
        }
        finally { resources.Backend.DestroyComputePipeline(pipeline); }
    }

    private static NativeShaderInputLayout PackageRootLayout() => new("VulkanRoot", 80, 16, [
        new("outputAddress", NativeShaderInputFieldKind.GpuAddress, 0, 8),
        new("value", NativeShaderInputFieldKind.Scalar, 8, 4),
        new("outputIndex", NativeShaderInputFieldKind.Scalar, 12, 4),
        new("bufferIndex", NativeShaderInputFieldKind.DescriptorIndex, 16, 4),
        new("textureIndex", NativeShaderInputFieldKind.DescriptorIndex, 20, 4),
        new("samplerIndex", NativeShaderInputFieldKind.DescriptorIndex, 24, 4),
        new("tail", NativeShaderInputFieldKind.Vector, 64, 16)
    ]);
}
