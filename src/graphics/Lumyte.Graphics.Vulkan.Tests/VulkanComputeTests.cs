using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Shader;

using Silk.NET.Shaderc;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
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
            GpuBufferUsage.Storage);
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
        GpuBufferView view = buffers.CreateView(
            buffer,
            new(Access: GpuBufferViewAccess.ReadWrite));
        var resources = new GpuResourceTable(0, 0, 0, 0, 1);
        resources.SetWritableBuffer(0, view.Id);

        IGpuQueue queue = device.MainQueue;
        GpuCommandBuffer commands = queue.StartCommandRecording()
            .SetComputePipeline(pipeline)
            .SetComputeResourceTable(resources)
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

        device.DestroyBufferView(view);
        buffers.Retire(buffer, memory, new(1));
        buffers.Collect(new(1));
        device.DestroyComputePipeline(pipeline);
        arena.VerifyEmpty();
    }

    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void ComputeWritesStorageResourcesThroughCommonResourceTable()
    {
        using VulkanDevice device = VulkanDevice.Create();
        using var textureArena = new GpuPersistentArena(device);
        using var bufferArena = new GpuPersistentArena(device);
        var textures = new GpuManualTextureAllocator(device, textureArena);
        var buffers = new GpuManualBufferAllocator(device, bufferArena);
        var textureDescription = new GpuTextureDescription(
            2,
            2,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.Storage | GpuTextureUsage.CopySource);
        GpuMemoryAllocation textureMemory = textures.AllocateMemory(textureDescription);
        GpuTextureHandle texture = textures.CreatePlacedTexture(textureDescription, textureMemory);
        GpuTextureView view = textures.CreateView(
            texture,
            new(GpuFormat.Rgba8Unorm, Access: GpuTextureViewAccess.ReadWrite));
        var readbackDescription = new GpuBufferDescription(16, GpuBufferUsage.CopyDestination);
        GpuMemoryAllocation readbackMemory = buffers.AllocateMemory(
            readbackDescription,
            GpuMemoryKind.HostCached);
        GpuBufferHandle readback = buffers.CreatePlacedBuffer(readbackDescription, readbackMemory);
        var writableDescription = new GpuBufferDescription(64, GpuBufferUsage.Storage);
        GpuMemoryAllocation writableMemory = buffers.AllocateMemory(
            writableDescription,
            GpuMemoryKind.DeviceLocal);
        GpuBufferHandle writable = buffers.CreatePlacedBuffer(writableDescription, writableMemory);
        GpuBufferView writableView = buffers.CreateView(
            writable,
            new(Access: GpuBufferViewAccess.ReadWrite));
        byte[] abiHash = GpuShaderBindingConvention.AbiHash.ToArray();
        GpuShaderPackage package = GpuShaderPackage.Read(GpuShaderPackageWriter.Write([
            new(
                GpuShaderCodeFormat.SpirV,
                GpuShaderStage.Compute,
                "writeStorageTexture",
                "vulkan",
                "spirv1.3",
                "",
                abiHash,
                TriangleShaders.Compile(
                    TriangleShaders.StorageTextureComputeSource,
                    ShaderKind.ComputeShader)),
        ]));
        GpuComputePipelineHandle pipeline = device.CreateComputePipeline(
            package,
            "writeStorageTexture",
            abiHash);
        var resources = new GpuResourceTable(0, 0, 0, 1, 1);
        resources.SetStorageTexture(0, view.Id);
        resources.SetWritableBuffer(0, writableView.Id);

        GpuCommandBuffer commands = device.MainQueue.StartCommandRecording()
            .SetComputePipeline(pipeline)
            .SetComputeResourceTable(resources)
            .Dispatch(2, 2)
            .Barrier(GpuStage.ComputeShader, GpuStage.Copy)
            .CopyTextureToMemory(texture, buffers.AddressOf(readback), new(2, 2, 4, 8));
        using GpuSemaphore completion = device.MainQueue.CreateSemaphore();
        device.MainQueue.Submit([commands], completion, 1);
        device.MainQueue.Wait(completion, 1);
        byte[] actual = readbackMemory.MappedBytes()[..16].ToArray();

        device.DestroyComputePipeline(pipeline);
        device.DestroyTextureView(view);
        device.DestroyBufferView(writableView);
        buffers.Retire(writable, writableMemory, new(1));
        buffers.Retire(readback, readbackMemory, new(1));
        buffers.Collect(new(1));
        textures.Retire(texture, textureMemory, new(1));
        textures.Collect(new(1));
        textures.VerifyEmpty();

        byte[] expected = Enumerable.Repeat(new byte[] { 64, 128, 191, 255 }, 4)
            .SelectMany(static value => value)
            .ToArray();
        Assert.True(
            actual.Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) <= 1),
            $"Expected [{string.Join(", ", expected)}] within 1, but was [{string.Join(", ", actual)}].");
    }
}
