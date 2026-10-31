using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Exercises actual WARP allocations, GPU transfers and public validation.</summary>
public sealed class TransferTests
{
    /// <summary>Checks byte-exact transfers across submissions and split command lists.</summary>
    /// <param name="split">Whether copies span two lists in one submission.</param>
    /// <returns>The asynchronous verification.</returns>
    [AgilityTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoundtripWithOffsetsAndStateDecayAsync(bool split)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<PlatformNotSupportedException>(() => Direct3D12Device.Create());
            return;
        }

        using var device = Direct3D12Device.Create(new() { UseWarp = true });
        string? expectedSdk = typeof(TransferTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "AgilitySdkVersion").Value;
        Assert.Equal(expectedSdk, device.SdkVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using IGraphicsBuffer<byte> upload = Create(device, MemoryPreference.Upload);
        using IGraphicsBuffer<byte> gpu = Create(device, MemoryPreference.Automatic);
        using IGraphicsBuffer<byte> readback = Create(device, MemoryPreference.Readback);
        for (int round = 0; round < 3; round++)
        {
            byte[] expected = Enumerable.Range(0, 257).Select(i => (byte)((i + round) % 251)).ToArray();
            await upload.MapAsync();
            upload.SliceElements(1, 257).CopyFrom(expected);
            upload.Unmap();
            using IGraphicsCommandBuffer first = device.CreateCommandBuffer(new());
            using IGraphicsCommandBuffer second = device.CreateCommandBuffer(new());
            first.Barrier(Barrier(gpu, ResourceAccess.None, ResourceAccess.CopyWrite));
            first.CopyBuffer(upload.SliceElements(1, 257), gpu.SliceElements(3, 257));
            IGraphicsCommandBuffer copyBack = split ? second : first;
            copyBack.Barrier(Barrier(gpu, ResourceAccess.CopyWrite, ResourceAccess.CopyRead));
            copyBack.CopyBuffer(gpu.SliceElements(3, 257), readback.SliceElements(5, 257));
            first.Finish();
            second.Finish();
            Assert.Throws<InvalidOperationException>(() => first.Finish());
            using IGraphicsSubmission submission = device.Queue.Submit(split ? [first, second] : [first]);
            Assert.Throws<ArgumentException>(() => device.Queue.Submit([first]));
            await submission.WaitAsync();
            Assert.Equal(SubmissionStatus.Completed, submission.Status);
            Assert.Equal(CommandBufferState.Completed, first.State);
            await readback.MapAsync();
            byte[] actual = new byte[257];
            readback.SliceElements(5, 257).CopyTo(actual);
            readback.Unmap();
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>Checks mapping direction, cancellation, byte bounds and GPU-use validation.</summary>
    /// <returns>The asynchronous verification.</returns>
    [AgilityFact]
    public async Task MappingAndCopyValidationAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var device = Direct3D12Device.Create(new() { UseWarp = true });
        using IGraphicsBuffer<byte> upload = Create(device, MemoryPreference.Upload);
        using IGraphicsBuffer<byte> gpu = Create(device, MemoryPreference.Automatic);
        using IGraphicsBuffer<byte> readback = Create(device, MemoryPreference.Readback);
        using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        Assert.Throws<InvalidOperationException>(() => upload.CopyFrom(new byte[1]));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await gpu.MapAsync());
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await upload.MapAsync(new(true)));
        Assert.False(upload.IsMapped);
        await upload.MapAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await upload.MapAsync());
        Assert.Throws<InvalidOperationException>(() => upload.CopyTo(new byte[300]));
        Assert.Throws<InvalidOperationException>(() => command.CopyBuffer(upload.SliceElements(0, 300), readback.SliceElements(0, 300)));
        upload.Unmap();
        Assert.Throws<ArgumentOutOfRangeException>(() => upload.SliceElements(299, 2));
        Assert.Throws<ArgumentException>(() => command.CopyBuffer(upload.SliceElements(0, 2), readback.SliceElements(0, 3)));
        Assert.Throws<ArgumentException>(() => command.CopyBuffer(readback.SliceElements(0, 3), gpu.SliceElements(0, 3)));
        Assert.Throws<NotSupportedException>(() => command.Barrier(new BufferBarrierDesc<byte> { Buffer = gpu.SliceElements(1, 2), Before = new(PipelineStage.None, ResourceAccess.None), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) }));
        command.Finish();
        Assert.Throws<ArgumentException>(() => device.Queue.Submit([command, command]));
        Assert.Equal(CommandBufferState.Executable, command.State);
        using IGraphicsSubmission submission = device.Queue.Submit([command]);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await submission.WaitAsync(new(true)));
        await submission.WaitAsync();
        Assert.Equal(SubmissionStatus.Completed, submission.Status);
    }

    /// <summary>Checks adapter selection, lifetime and ownership boundaries.</summary>
    [AgilityFact]
    public void DeviceAndOwnershipValidation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Throws<ArgumentException>(() => Direct3D12Device.Create(new() { UseWarp = true, AdapterIndex = 1 }));
        using var first = Direct3D12Device.Create(new() { UseWarp = true });
        using var second = Direct3D12Device.Create(new() { UseWarp = true });
        using IGraphicsBuffer<byte> source = Create(first, MemoryPreference.Upload);
        using IGraphicsBuffer<byte> destination = Create(second, MemoryPreference.Readback);
        using IGraphicsCommandBuffer command = first.CreateCommandBuffer(new());
        Assert.Throws<ArgumentException>(() => command.CopyBuffer(source.SliceElements(0, 1), destination.SliceElements(0, 1)));
        Assert.Throws<ArgumentException>(() => first.CreateBuffer(new BufferDesc<byte> { Count = 0, Usage = BufferUsage.CopySource }));
        Assert.Throws<NotSupportedException>(() => first.CreateBuffer(new BufferDesc<byte> { Count = 1, Usage = BufferUsage.ShaderWrite }));
        Assert.Throws<NotSupportedException>(() => first.CreateBuffer(new BufferDesc<byte> { Count = 1, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Upload }));
        command.Finish();
        Assert.Throws<ArgumentException>(() => second.Queue.Submit([command]));
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.SliceElements(0, 1));
        command.Dispose();
        first.Dispose();
        first.Dispose();
        Assert.Equal(GraphicsDeviceStatus.Disposed, first.Status);
        Assert.Throws<ObjectDisposedException>(() => first.GetBufferLayout<byte>());
        Assert.Equal(128UL * 1024 * 1024, first.Caps.MaxBufferSize);
    }

    private static IGraphicsBuffer<byte> Create(IGraphicDevice device, MemoryPreference memory) => device.CreateBuffer(new BufferDesc<byte>
    {
        Count = 300,
        Memory = memory,
        Usage = memory == MemoryPreference.Upload ? BufferUsage.CopySource : memory == MemoryPreference.Readback ? BufferUsage.CopyDestination : BufferUsage.CopySource | BufferUsage.CopyDestination,
    });

    private static BufferBarrierDesc<byte> Barrier(IGraphicsBuffer<byte> buffer, ResourceAccess before, ResourceAccess after) => new()
    {
        Buffer = buffer.SliceElements(0, buffer.Count),
        Before = new(before == ResourceAccess.None ? PipelineStage.None : PipelineStage.Copy, before),
        After = new(PipelineStage.Copy, after),
    };
}
