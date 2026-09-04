namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
public sealed class DirectX12DeviceTests
{
    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void NativeDeviceAndQueueCanBeCreated()
    {
        using DirectX12Device device = DirectX12Device.Create();

        Assert.NotEqual(0, device.NativeDevice);
        Assert.NotEqual(0, device.NativeQueue);
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void DeviceExposesExplicitRasterCapabilities()
    {
        using DirectX12Device device = DirectX12Device.Create();
        IGpuBackend backend = device;

        Assert.Equal(
            GpuBackendCapabilities.ExplicitPlacement
            | GpuBackendCapabilities.RasterPipeline
            | GpuBackendCapabilities.MemoryAliasing,
            backend.Capabilities);
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void BufferRoundTripPreservesBytes()
    {
        using DirectX12Device device = DirectX12Device.Create();
        byte[] expected = [0, 1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233, 17, 29, 47];

        byte[] actual = device.RoundTripBuffer(expected);

        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void BufferViewOwnsASeparateShaderDescriptorIdentity()
    {
        using DirectX12Device device = DirectX12Device.Create();
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
    [Trait("Category", "DirectX12Conformance")]
    public void PlacedTextureTransferPreservesPixels()
    {
        using DirectX12Device device = DirectX12Device.Create();
        var textureArena = new GpuPersistentArena(device);
        var bufferArena = new GpuPersistentArena(device);
        var textures = new GpuManualTextureAllocator(device, textureArena);
        var buffers = new GpuManualBufferAllocator(device, bufferArena);
        var textureDescription = new GpuTextureDescription(
            2, 2, GpuFormat.Rgba8Unorm,
            GpuTextureUsage.CopyDestination | GpuTextureUsage.CopySource);
        GpuMemoryAllocation textureMemory = textures.AllocateMemory(textureDescription);
        GpuTextureHandle texture = textures.CreatePlacedTexture(textureDescription, textureMemory);
        byte[] expected =
        [
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255,
        ];
        var uploadDescription = new GpuBufferDescription(16, GpuBufferUsage.CopySource);
        GpuMemoryAllocation uploadMemory = buffers.AllocateMemory(uploadDescription, GpuMemoryKind.HostMapped);
        GpuBufferHandle upload = buffers.CreatePlacedBuffer(uploadDescription, uploadMemory);
        expected.CopyTo(uploadMemory.MappedBytes());
        var readbackDescription = new GpuBufferDescription(16, GpuBufferUsage.CopyDestination);
        GpuMemoryAllocation readbackMemory = buffers.AllocateMemory(readbackDescription, GpuMemoryKind.HostCached);
        GpuBufferHandle readback = buffers.CreatePlacedBuffer(readbackDescription, readbackMemory);

        GpuCommandBuffer commands = device.MainQueue.StartCommandRecording()
            .CopyMemoryToTexture(buffers.AddressOf(upload), texture, new(2, 2, 4, 8))
            .Barrier(GpuStage.Copy, GpuStage.Copy)
            .CopyTextureToMemory(texture, buffers.AddressOf(readback), new(2, 2, 4, 8));
        using GpuSemaphore completion = device.MainQueue.CreateSemaphore();
        device.MainQueue.Submit([commands], completion, 1);
        device.MainQueue.Wait(completion, 1);
        byte[] actual = readbackMemory.MappedBytes()[..16].ToArray();

        Assert.Equal(expected, actual);

        buffers.Retire(upload, uploadMemory, new(1));
        buffers.Retire(readback, readbackMemory, new(1));
        buffers.Collect(new(1));
        textures.Retire(texture, textureMemory, new(1));
        textures.Collect(new(1));
        textures.VerifyEmpty();
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void NativeHeapClassesRemainExact()
    {
        using DirectX12Device device = DirectX12Device.Create();
        IGpuBackend backend = device;
        GpuBufferMemoryRequirements buffer = device.GetBufferMemoryRequirements(
            new(64, GpuBufferUsage.ShaderData));
        GpuTextureMemoryRequirements sampled = device.GetTextureMemoryRequirements(
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));
        GpuTextureMemoryRequirements attachment = device.GetTextureMemoryRequirements(
            new(4, 4, GpuFormat.Rgba8Unorm, GpuTextureUsage.ColorAttachment));

        bool bufferAndTexture = backend.TryCombineMemoryCompatibility(
            buffer.Compatibility,
            sampled.Compatibility,
            out _);
        bool sampledAndAttachment = backend.TryCombineMemoryCompatibility(
            sampled.Compatibility,
            attachment.Compatibility,
            out _);

        Assert.False(bufferAndTexture);
        Assert.False(sampledAndAttachment);
        Assert.NotEqual(buffer.Compatibility, sampled.Compatibility);
        Assert.NotEqual(sampled.Compatibility, attachment.Compatibility);
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void PlacedTextureRejectsAMisalignedRegion()
    {
        using DirectX12Device device = DirectX12Device.Create();
        var description = new GpuTextureDescription(
            4,
            4,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.Sampled);
        GpuTextureMemoryRequirements requirements = device.GetTextureMemoryRequirements(description);
        GpuMemoryAllocation backing = device.AllocateMemory(
            checked(requirements.Size + requirements.Alignment),
            requirements.Alignment,
            GpuMemoryKind.DeviceLocal,
            requirements.Compatibility);
        var misaligned = new GpuMemoryAllocation(
            requirements.Size,
            requirements.Alignment,
            GpuMemoryKind.DeviceLocal,
            0,
            new(
                backing.MemoryAddress.AllocationId,
                1,
                requirements.Size));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => device.CreatePlacedTexture(description, misaligned));

        Assert.Equal("MemoryAddress", exception.ParamName);
        device.FreeMemory(backing);
    }
}
