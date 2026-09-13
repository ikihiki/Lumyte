using System.Buffers;

namespace Lumyte.Graphics.Portable.Tests.Device;

public sealed partial class ExternalPortableGpuBackendTests
{
    [Fact]
    public async Task ConsumerWritesAnAsyncMappingBeforeUnmappingAndDestroyingTheBuffer()
    {
        List<object> observed = [];
        TaskCompletionSource<GpuMappedBufferRange> mapped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IPortableGpuBackend backend = new ExternalBackend(observed.Add, _ => new(mapped.Task));
        var description = new GpuBufferDescription((1ul << 40) + 256, GpuBufferUsage.MapWrite | GpuBufferUsage.CopySource);
        GpuBufferHandle buffer = backend.CreateBuffer(description);
        const ulong offset = (1ul << 36) + 8;
        byte[] storage = new byte[4];

        ValueTask<GpuMappedBufferRange> pending = backend.MapBufferAsync(buffer, GpuMapMode.Write, offset, 4);
        Assert.False(pending.IsCompleted);
        mapped.SetResult(new ExternalMappedRange(storage, GpuMapMode.Write, () => observed.Add(new Unmap(buffer))));
        using (GpuMappedBufferRange range = await pending)
        {
            new byte[] { 3, 5, 8, 13 }.CopyTo(range.Memory.Span);
        }
        backend.DestroyBuffer(buffer);

        Assert.Equal(new byte[] { 3, 5, 8, 13 }, storage);
        Assert.Collection(observed,
            value => Assert.Equal(description, Assert.IsType<BufferCreation>(value).Description),
            value => Assert.Equal(new MapRequest(buffer, GpuMapMode.Write, offset, 4), Assert.IsType<MapRequest>(value)),
            value => Assert.Equal(new Unmap(buffer), Assert.IsType<Unmap>(value)),
            value => Assert.Same(buffer, value));
    }

    [Fact]
    public async Task ConsumerReadsAMappingThroughReadOnlyMemory()
    {
        using IPortableGpuBackend backend = new ExternalBackend(_ => { }, request =>
            new(new ExternalMappedRange([21, 34, 55, 89], request.Mode, () => { })));
        GpuBufferHandle buffer = backend.CreateBuffer(new(256, GpuBufferUsage.MapRead | GpuBufferUsage.CopyDestination));
        byte[] actual;

        using (GpuMappedBufferRange range = await backend.MapBufferAsync(buffer, GpuMapMode.Read, 128, 4))
        {
            actual = range.ReadOnlyMemory.Slice(1, 2).ToArray();
        }
        backend.DestroyBuffer(buffer);

        Assert.Equal(new byte[] { 34, 55 }, actual);
    }

    // Managed backing makes the external consumer deterministic. Actual unmap, access
    // invalidation, and read-only enforcement are tested against the backend's manager.
    private sealed class ExternalMappedRange(byte[] storage, GpuMapMode mode, Action unmap) : GpuMappedBufferRange
    {
        private readonly MappedMemory memory = new(storage);
        private bool disposed;

        public override Memory<byte> Memory => mode == GpuMapMode.Write
            ? memory.Memory : throw new InvalidOperationException("The mapping is read-only.");
        public override ReadOnlyMemory<byte> ReadOnlyMemory => memory.Memory;

        public override void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            ((IDisposable)memory).Dispose();
            unmap();
        }
    }

    private sealed class MappedMemory(byte[] storage) : MemoryManager<byte>
    {
        private bool disposed;

        public override Span<byte> GetSpan()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return storage;
        }

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) => disposed = true;
    }
}
