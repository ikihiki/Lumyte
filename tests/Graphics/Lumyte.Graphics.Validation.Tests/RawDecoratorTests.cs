using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Validation.Tests;

/// <summary>Exercises raw buffer ownership, slice forwarding and host-write leases.</summary>
public sealed class RawDecoratorTests
{
    /// <summary>Mixed element types preserve byte offsets and native allocation identity.</summary>
    [Fact]
    public void CopySlicesUnwrapBothAllocations()
    {
        var backend = new RawBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsBuffer<int> source = Create<int>(device);
        object? nativeSource = backend.LastBuffer;
        using IGraphicsBuffer<byte> destination = Create<byte>(device);
        object? nativeDestination = backend.LastBuffer;
        using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        command.CopyBuffer(source.SliceElements(1, 1), destination.SliceElements(2, 4));
        Assert.Same(nativeSource, backend.Command.Source);
        Assert.Same(nativeDestination, backend.Command.Destination);
        Assert.Equal(4UL, backend.Command.SourceOffset);
        Assert.Equal(2UL, backend.Command.DestinationOffset);
        Assert.Equal(4UL, backend.Command.CopySize);
    }

    /// <summary>Foreign-domain buffers cannot reach native command recording.</summary>
    [Fact]
    public void ForeignCopyIsRejectedBeforeBackend()
    {
        var backend = new RawBackend();
        using var device = new ValidationDevice(backend);
        using var foreign = new ValidationDevice(new RawBackend());
        using IGraphicsBuffer<int> source = Create<int>(foreign);
        using IGraphicsBuffer<int> destination = Create<int>(device);
        using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        Assert.Throws<ArgumentException>(() => command.CopyBuffer(source.SliceElements(0, 1), destination.SliceElements(0, 1)));
        Assert.Null(backend.Command.Source);
    }

    /// <summary>Recorded use prevents mapping and release until command disposal.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task RecordedUseGuardsMappingAndReleaseAsync()
    {
        var backend = new RawBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsBuffer<int> source = Create<int>(device, MemoryPreference.Upload);
        RawBuffer<int> native = Assert.IsType<RawBuffer<int>>(backend.LastBuffer);
        using IGraphicsBuffer<int> destination = Create<int>(device);
        using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        command.CopyBuffer(source.SliceElements(0, 1), destination.SliceElements(0, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.MapAsync());
        Assert.Throws<InvalidOperationException>(source.Dispose);
        Assert.Equal(0, native.Maps);
        Assert.Equal(0, native.Disposals);
        command.Dispose();
        await source.MapAsync();
        Assert.True(source.IsMapped);
        Assert.Equal(1, native.Maps);
    }

    /// <summary>Host writes require the matching mapping and validate caller ranges.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task MappingAndRangeChecksPrecedeNativeWritesAsync()
    {
        var backend = new RawBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsBuffer<int> buffer = Create<int>(device, MemoryPreference.Upload);
        RawBuffer<int> native = Assert.IsType<RawBuffer<int>>(backend.LastBuffer);
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom(new int[] { 1 }));
        await buffer.MapAsync();
        Assert.Throws<ArgumentException>(() => buffer.CopyBytesFrom(new byte[5], 0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.CopyBytesFrom(new byte[1], ulong.MaxValue, 4));
        buffer.CopyFrom([]);
        Assert.Equal(0, native.Writes);
        buffer.SliceElements(2, 2).CopyFrom(new int[] { 7 });
        Assert.Equal(1, native.Writes);
        int[] contents = new int[8];
        native.CopyTo(contents);
        Assert.Equal(7, contents[2]);
        Assert.Equal(0, contents[3]);
        Assert.Throws<InvalidOperationException>(() => buffer.CopyTo(new int[8]));
    }

    /// <summary>Active callbacks forbid overlapping writes, remapping, unmapping and release.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task ReentrantHostWriteGuardsAndFailureCleanupAsync()
    {
        var backend = new RawBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsBuffer<int> buffer = Create<int>(device, MemoryPreference.Upload);
        RawBuffer<int> native = Assert.IsType<RawBuffer<int>>(backend.LastBuffer);
        await buffer.MapAsync();
        native.OnWrite = () =>
        {
            Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom(new int[] { 9 }));
            Assert.Throws<InvalidOperationException>(buffer.Unmap);
            Assert.Throws<InvalidOperationException>(buffer.Dispose);
            Assert.True(buffer.MapAsync().AsTask().IsFaulted);
            throw new InvalidOperationException("Injected native write failure.");
        };
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom(new int[] { 1 }));
        Assert.Equal(1, native.Writes);
        Assert.Equal(1, native.Maps);
        Assert.Equal(0, native.Disposals);
        native.OnWrite = null;
        buffer.CopyFrom(new int[] { 2 });
        buffer.Unmap();
        Assert.False(buffer.IsMapped);
        buffer.Dispose();
        Assert.Equal(1, native.Disposals);
    }

    /// <summary>Disjoint byte writes are allowed while only actual written bytes are leased.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task DisjointReentrantSliceWritePreservesBothValuesAsync()
    {
        var backend = new RawBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsBuffer<int> buffer = Create<int>(device, MemoryPreference.Upload);
        RawBuffer<int> native = Assert.IsType<RawBuffer<int>>(backend.LastBuffer);
        await buffer.MapAsync();
        native.OnWrite = () =>
        {
            native.OnWrite = null;
            buffer.SliceElements(1, 1).CopyFrom(new int[] { 22 });
        };
        buffer.SliceElements(0, 4).CopyFrom(new int[] { 11 });
        int[] contents = new int[8];
        native.CopyTo(contents);
        Assert.Equal(11, contents[0]);
        Assert.Equal(22, contents[1]);
        Assert.Equal(2, native.Writes);
        buffer.Unmap();
    }

    /// <summary>Readback paths preserve slice offsets and reject upload-only operations.</summary>
    /// <returns>The asynchronous assertions.</returns>
    [Fact]
    public async Task ReadbackSlicesForwardCorrectByteRangeAsync()
    {
        var backend = new RawBackend();
        using var device = new ValidationDevice(backend);
        using IGraphicsBuffer<int> buffer = Create<int>(device, MemoryPreference.Readback);
        RawBuffer<int> native = Assert.IsType<RawBuffer<int>>(backend.LastBuffer);
        native.CopyFrom(new int[] { 10, 20, 30, 40 });
        Assert.Throws<InvalidOperationException>(() => buffer.CopyTo(new int[8]));
        await buffer.MapAsync();
        int[] values = new int[2];
        buffer.SliceElements(1, 2).CopyTo(values);
        Assert.Equal(new int[] { 20, 30 }, values);
        Assert.Throws<ArgumentException>(() => buffer.CopyTo(new int[1]));
        Assert.Throws<InvalidOperationException>(() => buffer.CopyFrom(new int[] { 5 }));
    }

    private static IGraphicsBuffer<T> Create<T>(ValidationDevice device, MemoryPreference memory = MemoryPreference.Automatic)
        where T : unmanaged => device.CreateBuffer(new BufferDesc<T> { Count = 8, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination, Memory = memory });
}
