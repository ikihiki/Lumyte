using System.Security.Cryptography;
using System.Text;

using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Shader;

using Silk.NET.Shaderc;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
public sealed class VulkanDeviceConformanceTests
{
    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void DeviceExposesExplicitRasterCapabilities()
    {
        using VulkanDevice device = VulkanDevice.Create();
        IGpuBackend backend = device;

        Assert.Equal(
            GpuBackendCapabilities.ExplicitPlacement
            | GpuBackendCapabilities.RasterPipeline
            | GpuBackendCapabilities.MemoryAliasing
            | GpuBackendCapabilities.ComputePipeline,
            backend.Capabilities);
    }

    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void BufferViewOwnsASeparateShaderDescriptorIdentity()
    {
        using VulkanDevice device = VulkanDevice.Create();
        using var arena = new GpuPersistentArena(device);
        var buffers = new GpuManualBufferAllocator(device, arena);
        var description = new GpuBufferDescription(64, GpuBufferUsage.ShaderData);
        GpuMemoryAllocation memory = buffers.AllocateMemory(description, GpuMemoryKind.DeviceLocal);
        GpuBufferHandle buffer = buffers.CreatePlacedBuffer(description, memory);

        GpuBufferView view = buffers.CreateView(buffer, new(16, 32));

        Assert.False(view.Id.IsNull);
        Assert.Equal(buffer, view.Buffer);
        Assert.Equal(new GpuBufferViewDescription(16, 32), view.Description);
        Assert.Throws<InvalidOperationException>(() => device.DestroyBuffer(buffer));

        device.DestroyBufferView(view);
        buffers.Retire(buffer, memory, new(0));
        buffers.Collect(new(0));
        arena.VerifyEmpty();
    }

    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void MemoryTypeMasksSelectACommonArenaKey()
    {
        using VulkanDevice device = VulkanDevice.Create();
        IGpuBackend backend = device;
        GpuTextureMemoryRequirements sampled = device.GetTextureMemoryRequirements(
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));
        GpuTextureMemoryRequirements attachment = device.GetTextureMemoryRequirements(
            new(8, 8, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));

        bool compatible = backend.TryCombineMemoryCompatibility(
            sampled.Compatibility,
            attachment.Compatibility,
            out ulong combined);
        ulong allocationKey = backend.GetMemoryCompatibilityKey(
            GpuMemoryKind.DeviceLocal,
            combined);

        Assert.True(compatible);
        Assert.NotEqual(0ul, combined);
        Assert.True(System.Numerics.BitOperations.IsPow2(allocationKey));
        Assert.True(backend.IsMemoryCompatibilityKeyCompatible(
            GpuMemoryKind.DeviceLocal,
            allocationKey,
            sampled.Compatibility));
        Assert.True(backend.IsMemoryCompatibilityKeyCompatible(
            GpuMemoryKind.DeviceLocal,
            allocationKey,
            attachment.Compatibility));
        Assert.False(backend.TryCombineMemoryCompatibility(0b0001, 0b0010, out _));
    }

    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void DynamicRenderingTriangleCanBeReadBack()
    {
        using VulkanDevice device = VulkanDevice.Create();
        var textureArena = new GpuPersistentArena(device);
        var bufferArena = new GpuPersistentArena(device);
        var textures = new GpuManualTextureAllocator(device, textureArena);
        var buffers = new GpuManualBufferAllocator(device, bufferArena);
        var description = new GpuTextureDescription(
            64,
            64,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);
        GpuMemoryAllocation memory = textures.AllocateMemory(description);
        GpuTextureHandle texture = textures.CreatePlacedTexture(description, memory);
        GpuTextureView view = textures.CreateView(texture, new(GpuFormat.Rgba8Unorm));
        GpuColorAttachment attachment = textures.ColorAttachment(
            view,
            GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Store,
            new(0, 0, 0, 1));

        var readbackDescription = new GpuBufferDescription(64 * 64 * 4, GpuBufferUsage.CopyDestination);
        GpuMemoryAllocation readbackMemory = buffers.AllocateMemory(readbackDescription, GpuMemoryKind.HostCached);
        GpuBufferHandle readback = buffers.CreatePlacedBuffer(readbackDescription, readbackMemory);
        GpuMemoryAddress readbackAddress = buffers.AddressOf(readback, 0, 64 * 64 * 4);
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("triangle-v1"));
        byte[] packageBytes = GpuShaderPackageWriter.Write([
            new(GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "triangleVertex", "vulkan", "spirv1.3", "", abiHash,
                TriangleShaders.Compile(TriangleShaders.VertexSource, ShaderKind.VertexShader)),
            new(GpuShaderCodeFormat.SpirV, GpuShaderStage.Pixel, "trianglePixel", "vulkan", "spirv1.3", "", abiHash,
                TriangleShaders.Compile(TriangleShaders.PixelSource, ShaderKind.FragmentShader)),
            new(GpuShaderCodeFormat.Wgsl, GpuShaderStage.Vertex, "triangleVertex", "webgpu", "wgsl", "", abiHash,
                "@vertex fn triangleVertex() -> @builtin(position) vec4f { return vec4f(); }"u8.ToArray()),
            new(GpuShaderCodeFormat.Wgsl, GpuShaderStage.Pixel, "trianglePixel", "webgpu", "wgsl", "", abiHash,
                "@fragment fn trianglePixel() -> @location(0) vec4f { return vec4f(1); }"u8.ToArray())]);
        GpuShaderPackage package = GpuShaderPackage.Read(packageBytes);
        GpuRasterPipelineHandle pipeline = device.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]), package,
            "triangleVertex", "trianglePixel", abiHash);
        IGpuQueue queue = device.MainQueue;
        GpuCommandBuffer commands = queue.StartCommandRecording()
            .Barrier(GpuStage.None, GpuStage.ColorOutput)
            .BeginRendering([attachment])
            .SetPipeline(pipeline)
            .SetViewportAndScissor(new(0, 0, 64, 64), new(0, 0, 64, 64))
            .Draw(3)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToMemory(texture, readbackAddress, new(64, 64, 4, 256));

        using GpuSemaphore completion = queue.CreateSemaphore();
        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);
        queue.Wait(completion, 1);
        GpuCommandBuffer second = queue.StartCommandRecording()
            .Barrier(GpuStage.None, GpuStage.All);
        GpuCommandBuffer third = queue.StartCommandRecording()
            .Barrier(GpuStage.None, GpuStage.All);
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Submit([second], completion, 1));
        queue.Submit([second, third], completion, 2);
        queue.Wait(completion, 2);
        byte[] pixels = readbackMemory.MappedBytes().ToArray();

        Assert.Equal(64 * 64 * 4, pixels.Length);
        int center = (32 * 64 + 32) * 4;
        Assert.InRange(pixels[center], 253, 255);
        Assert.InRange(pixels[center + 1], 50, 52);
        Assert.InRange(pixels[center + 2], 24, 27);
        Assert.Equal(255, pixels[center + 3]);
        Assert.Equal([0, 0, 0, 255], pixels[..4]);

        buffers.Retire(readback, readbackMemory, new(1));
        buffers.Collect(new(1));
        device.DestroyRasterPipeline(pipeline);
        device.DestroyTextureView(view);
        textures.Retire(texture, memory, new(1));
        textures.Collect(new(1));
        textures.VerifyEmpty();
    }


    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void UploadedTextureCanBeSampledByAQuad()
    {
        using VulkanDevice device = VulkanDevice.Create();
        var textureArena = new GpuPersistentArena(device);
        var bufferArena = new GpuPersistentArena(device);
        var textures = new GpuManualTextureAllocator(device, textureArena);
        var buffers = new GpuManualBufferAllocator(device, bufferArena);

        var sampledDescription = new GpuTextureDescription(
            2, 2, GpuFormat.Rgba8Unorm,
            GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination);
        GpuMemoryAllocation sampledMemory = textures.AllocateMemory(sampledDescription);
        GpuTextureHandle sampledTexture = textures.CreatePlacedTexture(sampledDescription, sampledMemory);
        GpuTextureView sampledView = textures.CreateView(sampledTexture, new(GpuFormat.Rgba8Unorm));
        const uint textureIndex = 2;
        const uint samplerIndex = 3;
        SamplerId sampler = device.CreateSampler(default);
        var resources = new GpuResourceTable(4, 4);
        resources.SetTexture(checked((int)textureIndex), sampledView.Id);
        resources.SetSampler(checked((int)samplerIndex), sampler);

        byte[] texels =
        [
            255, 0, 0, 255,      0, 255, 0, 255,
            0, 0, 255, 255,      255, 255, 255, 255,
        ];
        var uploadDescription = new GpuBufferDescription((ulong)texels.Length, GpuBufferUsage.CopySource);
        GpuMemoryAllocation uploadMemory = buffers.AllocateMemory(uploadDescription, GpuMemoryKind.HostMapped);
        GpuBufferHandle upload = buffers.CreatePlacedBuffer(uploadDescription, uploadMemory);
        texels.CopyTo(uploadMemory.MappedBytes());
        GpuMemoryAddress uploadAddress = buffers.AddressOf(upload, 0, (ulong)texels.Length);

        var targetDescription = new GpuTextureDescription(
            64, 64, GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);
        GpuMemoryAllocation targetMemory = textures.AllocateMemory(targetDescription);
        GpuTextureHandle target = textures.CreatePlacedTexture(targetDescription, targetMemory);
        GpuTextureView targetView = textures.CreateView(target, new(GpuFormat.Rgba8Unorm));
        GpuColorAttachment attachment = textures.ColorAttachment(
            targetView, GpuAttachmentLoadOperation.Clear, GpuAttachmentStoreOperation.Store,
            new(0, 0, 0, 1));

        var readbackDescription = new GpuBufferDescription(64 * 64 * 4, GpuBufferUsage.CopyDestination);
        GpuMemoryAllocation readbackMemory = buffers.AllocateMemory(readbackDescription, GpuMemoryKind.HostCached);
        GpuBufferHandle readback = buffers.CreatePlacedBuffer(readbackDescription, readbackMemory);
        GpuMemoryAddress readbackAddress = buffers.AddressOf(readback, 0, 64 * 64 * 4);

        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("textured-quad-v1"));
        GpuShaderPackage package = GpuShaderPackage.Read(GpuShaderPackageWriter.Write([
            new(GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "quadVertex", "vulkan", "spirv1.3", "", abiHash,
                TriangleShaders.Compile(TriangleShaders.TexturedVertexSource, ShaderKind.VertexShader)),
            new(GpuShaderCodeFormat.SpirV, GpuShaderStage.Pixel, "quadPixel", "vulkan", "spirv1.3", "", abiHash,
                TriangleShaders.Compile(TriangleShaders.TexturedPixelSource, ShaderKind.FragmentShader)),
        ]));
        GpuRasterPipelineHandle pipeline = device.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]), package,
            "quadVertex", "quadPixel", abiHash);

        IGpuQueue queue = device.MainQueue;
        GpuCommandBuffer commands = queue.StartCommandRecording()
            .CopyMemoryToTexture(uploadAddress, sampledTexture, new(2, 2, 4, 8))
            .Barrier(GpuStage.Copy, GpuStage.PixelShader)
            .BeginRendering([attachment])
            .SetPipeline(pipeline)
            .SetResourceTable(resources)
            .SetRootData([.. BitConverter.GetBytes(textureIndex), .. BitConverter.GetBytes(samplerIndex)])
            .SetViewportAndScissor(new(0, 0, 64, 64), new(0, 0, 64, 64))
            .Draw(6)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToMemory(target, readbackAddress, new(64, 64, 4, 256));
        using GpuSemaphore completion = queue.CreateSemaphore();
        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        Span<byte> pixels = readbackMemory.MappedBytes();
        AssertPixel(pixels, 64, 16, 16, 255, 0, 0, 255);
        AssertPixel(pixels, 64, 48, 16, 0, 255, 0, 255);
        AssertPixel(pixels, 64, 16, 48, 0, 0, 255, 255);
        AssertPixel(pixels, 64, 48, 48, 255, 255, 255, 255);

        buffers.Retire(readback, readbackMemory, new(1));
        buffers.Retire(upload, uploadMemory, new(1));
        buffers.Collect(new(1));
        device.DestroyRasterPipeline(pipeline);
        device.DestroySampler(sampler);
        device.DestroyTextureView(sampledView);
        device.DestroyTextureView(targetView);
        textures.Retire(sampledTexture, sampledMemory, new(1));
        textures.Retire(target, targetMemory, new(1));
        textures.Collect(new(1));
        textures.VerifyEmpty();
    }

    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void BufferIdCanBeReadByAPixelShader()
    {
        using VulkanDevice device = VulkanDevice.Create();
        var textureArena = new GpuPersistentArena(device);
        var bufferArena = new GpuPersistentArena(device);
        var textures = new GpuManualTextureAllocator(device, textureArena);
        var buffers = new GpuManualBufferAllocator(device, bufferArena);
        var shaderDescription = new GpuBufferDescription(
            16,
            GpuBufferUsage.ShaderData | GpuBufferUsage.CopySource);
        GpuMemoryAllocation shaderMemory = buffers.AllocateMemory(
            shaderDescription,
            GpuMemoryKind.HostMapped);
        GpuBufferHandle shaderBuffer = buffers.CreatePlacedBuffer(shaderDescription, shaderMemory);
        GpuBufferView shaderView = buffers.CreateView(shaderBuffer);
        byte[] color =
        [
            .. BitConverter.GetBytes(1f),
            .. BitConverter.GetBytes(0.25f),
            .. BitConverter.GetBytes(0.5f),
            .. BitConverter.GetBytes(1f),
        ];
        color.CopyTo(shaderMemory.MappedBytes());
        var resources = new GpuResourceTable(0, 0, 1);
        resources.SetBuffer(0, shaderView.Id);

        var targetDescription = new GpuTextureDescription(
            64, 64, GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);
        GpuMemoryAllocation targetMemory = textures.AllocateMemory(targetDescription);
        GpuTextureHandle target = textures.CreatePlacedTexture(targetDescription, targetMemory);
        GpuTextureView targetView = textures.CreateView(target, new(GpuFormat.Rgba8Unorm));
        var readbackDescription = new GpuBufferDescription(64 * 64 * 4, GpuBufferUsage.CopyDestination);
        GpuMemoryAllocation readbackMemory = buffers.AllocateMemory(
            readbackDescription,
            GpuMemoryKind.HostCached);
        GpuBufferHandle readback = buffers.CreatePlacedBuffer(readbackDescription, readbackMemory);
        byte[] abiHash = GpuShaderBindingConvention.AbiHash.ToArray();
        GpuShaderPackage package = GpuShaderPackage.Read(GpuShaderPackageWriter.Write([
            new(GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "triangleVertex",
                "vulkan", "spirv1.3", "", abiHash,
                TriangleShaders.Compile(TriangleShaders.VertexSource, ShaderKind.VertexShader)),
            new(GpuShaderCodeFormat.SpirV, GpuShaderStage.Pixel, "bufferPixel",
                "vulkan", "spirv1.3", "", abiHash,
                TriangleShaders.Compile(TriangleShaders.BufferPixelSource, ShaderKind.FragmentShader)),
        ]));
        GpuRasterPipelineHandle pipeline = device.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]),
            package,
            "triangleVertex",
            "bufferPixel",
            abiHash);

        GpuCommandBuffer commands = device.MainQueue.StartCommandRecording()
            .Barrier(GpuStage.None, GpuStage.ColorOutput)
            .BeginRendering([
                new(targetView, GpuAttachmentLoadOperation.Clear, GpuAttachmentStoreOperation.Store,
                    new(0, 0, 0, 1)),
            ])
            .SetPipeline(pipeline)
            .SetResourceTable(resources)
            .SetViewportAndScissor(new(0, 0, 64, 64), new(0, 0, 64, 64))
            .Draw(3)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToMemory(target, buffers.AddressOf(readback), new(64, 64, 4, 256));
        using GpuSemaphore completion = device.MainQueue.CreateSemaphore();
        device.MainQueue.Submit([commands], completion, 1);
        device.MainQueue.Wait(completion, 1);
        byte[] pixels = readbackMemory.MappedBytes().ToArray();

        AssertPixelNear(pixels, 64, 32, 32, 255, 64, 128, 255);

        device.DestroyRasterPipeline(pipeline);
        device.DestroyBufferView(shaderView);
        buffers.Retire(shaderBuffer, shaderMemory, new(1));
        buffers.Retire(readback, readbackMemory, new(1));
        buffers.Collect(new(1));
        bufferArena.VerifyEmpty();
        device.DestroyTextureView(targetView);
        textures.Retire(target, targetMemory, new(1));
        textures.Collect(new(1));
        textures.VerifyEmpty();
    }

    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void UnsupportedPipelineOptionsAreRejected()
    {
        using VulkanDevice device = VulkanDevice.Create();
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("vulkan-unsupported-pipeline"));
        GpuShaderPackage package = GpuShaderPackage.Read(GpuShaderPackageWriter.Write([
            new(GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "triangleVertex", "vulkan", "spirv1.3", "", abiHash,
                TriangleShaders.Compile(TriangleShaders.VertexSource, ShaderKind.VertexShader)),
            new(GpuShaderCodeFormat.SpirV, GpuShaderStage.Pixel, "trianglePixel", "vulkan", "spirv1.3", "", abiHash,
                TriangleShaders.Compile(TriangleShaders.PixelSource, ShaderKind.FragmentShader)),
        ]));
        GpuRasterPipelineDescription[] descriptions =
        [
            new([new(GpuFormat.Rgba8Unorm)]) { SampleCount = 2 },
            new([new(GpuFormat.Rgba8Unorm)]) { AlphaToCoverage = true },
            new([new(GpuFormat.Rgba8Unorm)]) { SupportsDualSourceBlending = true },
            new([new(GpuFormat.Rgba8Unorm), new(GpuFormat.Rgba8Unorm)]),
        ];

        Assert.All(descriptions, description =>
            Assert.Throws<NotSupportedException>(() => device.CreateRasterPipeline(
                description, package, "triangleVertex", "trianglePixel", abiHash)));
    }

    private static void AssertPixel(
        ReadOnlySpan<byte> pixels, int width, int x, int y,
        byte red, byte green, byte blue, byte alpha)
    {
        int offset = (y * width + x) * 4;
        Assert.Equal(new byte[] { red, green, blue, alpha }, pixels.Slice(offset, 4).ToArray());
    }

    private static void AssertPixelNear(
        ReadOnlySpan<byte> pixels, int width, int x, int y,
        byte red, byte green, byte blue, byte alpha)
    {
        int offset = (y * width + x) * 4;
        Assert.InRange(pixels[offset], red - 1, red);
        Assert.InRange(pixels[offset + 1], green - 1, green);
        Assert.InRange(pixels[offset + 2], blue - 1, blue);
        Assert.InRange(pixels[offset + 3], alpha - 1, alpha);
    }
}
