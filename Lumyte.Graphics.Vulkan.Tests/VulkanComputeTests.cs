using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using Lumyte.Graphics.Vulkan;
using Lumyte.Shaders;

using Silk.NET.Shaderc;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed class VulkanComputeTests
{
    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void ComputeShaderWritesStorageBuffer()
    {
        const int elementCount = 64;
        using VulkanDevice device = VulkanDevice.Create();
        var arena = new GpuPersistentArena(device);
        var buffers = new GpuManualBufferAllocator(device, arena);
        var description = new GpuBufferDescription(
            elementCount * sizeof(uint),
            GpuBufferUsage.ShaderData);
        GpuMemoryAllocation memory = buffers.AllocateMemory(description, GpuMemoryKind.HostMapped);
        GpuBufferHandle buffer = buffers.CreatePlacedBuffer(description, memory);
        memory.MappedBytes().Clear();

        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("vulkan-compute-storage-buffer-v1"));
        GpuShaderPackage package = GpuShaderPackage.Read(GpuShaderPackageWriter.Write([
            new(
                GpuShaderCodeFormat.SpirV,
                GpuShaderStage.Compute,
                "writeValues",
                "vulkan",
                "spirv1.3",
                "",
                abiHash,
                TriangleShaders.Compile(TriangleShaders.ComputeSource, ShaderKind.ComputeShader)),
        ]));
        GpuComputePipelineHandle pipeline = device.CreateComputePipeline(package, "writeValues", abiHash);

        IGpuQueue queue = device.MainQueue;
        GpuCommandBuffer commands = queue.StartCommandRecording()
            .SetComputePipeline(pipeline)
            .SetComputeBuffer(0, buffer)
            .Dispatch(elementCount / 8)
            .Barrier(GpuStage.ComputeShader, GpuStage.All);
        using GpuSemaphore completion = queue.CreateSemaphore();
        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);
        uint[] actual = MemoryMarshal.Cast<byte, uint>(memory.MappedBytes()).ToArray();
        uint[] expected = Enumerable.Range(0, elementCount)
            .Select(index => 0x5a000000u | (checked((uint)index) * 17u + 3u))
            .ToArray();

        Assert.Equal(expected, actual);

        buffers.Retire(buffer, memory, new(1));
        buffers.Collect(new(1));
        device.DestroyComputePipeline(pipeline);
        arena.VerifyEmpty();
    }
}
