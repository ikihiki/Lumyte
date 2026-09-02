using Lumyte.Graphics;

namespace Lumyte.Graphics.Tests;

public sealed class GpuManualTextureAllocatorTests
{
    [Fact]
    public void ArenaRejectsBackendWithoutExplicitPlacement()
    {
        var backend = new UnsupportedBackend();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new GpuPersistentArena(backend));

        Assert.Equal("backend", exception.ParamName);
    }

    [Fact]
    public void ManualPathBuildsAttachmentFromPlacedTexture()
    {
        var backend = new RecordingResourceBackend();
        var arena = new GpuPersistentArena(backend);
        var textures = new GpuManualTextureAllocator(backend, arena);
        var description = new GpuTextureDescription(
            128,
            64,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.Sampled);

        GpuTextureMemoryRequirements requirements = textures.GetMemoryRequirements(description);
        GpuMemoryAllocation memory = textures.AllocateMemory(description);
        GpuTextureHandle texture = textures.CreatePlacedTexture(description, memory);
        GpuTextureView view = textures.CreateView(texture, new(GpuFormat.Rgba8Unorm));
        GpuColorAttachment attachment = textures.ColorAttachment(
            view,
            GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Store,
            new(0.1f, 0.2f, 0.3f, 1));

        Assert.Equal(32768ul, requirements.Size);
        Assert.Equal(GpuMemoryKind.DeviceLocal, memory.Kind);
        Assert.Equal(texture, attachment.View.Texture);
        Assert.Equal(GpuAttachmentLoadOperation.Clear, attachment.LoadOperation);
    }

    [Fact]
    public void CollectionWaitsForFenceAndDestroysTextureBeforeMemory()
    {
        var backend = new RecordingResourceBackend();
        var arena = new GpuPersistentArena(backend);
        var textures = new GpuManualTextureAllocator(backend, arena);
        var description = new GpuTextureDescription(
            4,
            4,
            GpuFormat.D32Float,
            GpuTextureUsage.DepthStencilAttachment);
        GpuMemoryAllocation memory = textures.AllocateMemory(description);
        GpuTextureHandle texture = textures.CreatePlacedTexture(description, memory);

        textures.Retire(texture, memory, new(7));
        int beforeFence = textures.Collect(new(6));
        int atFence = textures.Collect(new(7));

        Assert.Equal(0, beforeFence);
        Assert.Equal(1, atFence);
        Assert.Collection(
            backend.ReleaseEvents,
            value => Assert.Equal($"texture:{texture.Value}", value),
            value => Assert.Equal($"memory:{memory.MemoryAddress.AllocationId}", value));
        textures.VerifyEmpty();
    }

    [Fact]
    public void PlacedTextureRejectsUndersizedAllocation()
    {
        var backend = new RecordingResourceBackend();
        var arena = new GpuPersistentArena(backend);
        var textures = new GpuManualTextureAllocator(backend, arena);
        var description = new GpuTextureDescription(
            16,
            16,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment);
        var allocation = new GpuMemoryAllocation(
            128,
            256,
            GpuMemoryKind.DeviceLocal,
            0,
            new(0x1000, 0, 128));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => textures.CreatePlacedTexture(description, allocation));

        Assert.Equal("allocation", exception.ParamName);
    }

    [Fact]
    public void ArenaRejectsDuplicateRetirement()
    {
        var backend = new RecordingResourceBackend();
        var arena = new GpuPersistentArena(backend);
        GpuMemoryAllocation allocation = arena.Allocate(256, 16, GpuMemoryKind.DeviceLocal);
        arena.Retire(allocation, new(1));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => arena.Retire(allocation, new(2)));

        Assert.Equal("allocation", exception.ParamName);
    }

    [Fact]
    public void BufferAddressValidatesRangeAndRetirement()
    {
        var backend = new RecordingResourceBackend();
        var arena = new GpuPersistentArena(backend);
        var buffers = new GpuManualBufferAllocator(backend, arena);
        var description = new GpuBufferDescription(64, GpuBufferUsage.CopyDestination);
        GpuMemoryAllocation memory = buffers.AllocateMemory(description, GpuMemoryKind.HostCached);
        GpuBufferHandle buffer = buffers.CreatePlacedBuffer(description, memory);

        GpuMemoryAddress address = buffers.AddressOf(buffer, 16, 32);

        Assert.Equal(new GpuMemoryAddress(memory.MemoryAddress.AllocationId, 16, 32), address);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffers.AddressOf(buffer, 48, 32));
        buffers.Retire(buffer, memory, new(1));
        Assert.Throws<ArgumentException>(() => buffers.AddressOf(buffer));
    }

    private sealed class RecordingResourceBackend : IGpuBackend
    {
        private ulong nextAddress = 0x1000;
        private ulong nextTexture = 1;
        private readonly Dictionary<ulong, ulong> bufferAllocations = [];

        public List<string> ReleaseEvents { get; } = [];
        public GpuBackendCapabilities Capabilities => GpuBackendCapabilities.ExplicitPlacement;

        public GpuMemoryAllocation AllocateMemory(ulong size, ulong alignment, GpuMemoryKind kind)
        {
            ulong address = nextAddress;
            nextAddress += Align(size, alignment);
            nint cpuAddress = kind == GpuMemoryKind.DeviceLocal ? 0 : checked((nint)(address + 0x100000));
            return new(size, alignment, kind, cpuAddress, new(address, 0, size));
        }

        public void FreeMemory(GpuMemoryAllocation allocation)
            => ReleaseEvents.Add($"memory:{allocation.MemoryAddress.AllocationId}");

        public GpuTextureMemoryRequirements GetTextureMemoryRequirements(GpuTextureDescription description)
            => new(checked((ulong)description.Width * description.Height * 4), 256);

        public GpuTextureHandle CreatePlacedTexture(
            GpuTextureDescription description,
            GpuMemoryAllocation allocation)
            => new(nextTexture++);

        public void DestroyTexture(GpuTextureHandle texture)
            => ReleaseEvents.Add($"texture:{texture.Value}");

        public GpuTextureView CreateTextureView(
            GpuTextureHandle texture,
            GpuTextureViewDescription description)
            => new(new(nextTexture++), texture, description);

        public void DestroyTextureView(GpuTextureView view) { }

        public SamplerId CreateSampler(GpuSamplerDescription description) => new(nextTexture++);
        public void DestroySampler(SamplerId sampler) => ReleaseEvents.Add($"sampler:{sampler.Value}");

        public GpuBufferMemoryRequirements GetBufferMemoryRequirements(GpuBufferDescription description)
            => new(description.Size, 16);

        public GpuBufferHandle CreatePlacedBuffer(
            GpuBufferDescription description,
            GpuMemoryAllocation allocation)
        {
            var buffer = new GpuBufferHandle(nextTexture++, description.Size);
            bufferAllocations.Add(buffer.Value, allocation.MemoryAddress.AllocationId);
            return buffer;
        }

        public void DestroyBuffer(GpuBufferHandle buffer)
            => ReleaseEvents.Add($"buffer:{buffer.Value}");

        public GpuMemoryAddress GetBufferMemoryAddress(GpuBufferHandle buffer, ulong offset, ulong length)
            => new(bufferAllocations[buffer.Value], offset, length);

        private static ulong Align(ulong value, ulong alignment)
            => checked((value + alignment - 1) & ~(alignment - 1));
    }

    private sealed class UnsupportedBackend : IGpuBackend
    {
        public GpuBackendCapabilities Capabilities => GpuBackendCapabilities.None;
    }
}
