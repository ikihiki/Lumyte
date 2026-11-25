using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Pipelines.Tests;

/// <summary>Checks readback dependencies without creating a native device or submitting work.</summary>
public sealed class TransferSynchronizationTests
{
    /// <summary>Combines command recording with the mandatory same-device origin contract.</summary>
    public interface IOriginCommands : IGraphicsCommandBuffer, IGraphicsNativeOrigin;

    /// <summary>The exact copied readback range must become HostRead before mapping.</summary>
    /// <returns>Completion of the managed mapping and cleanup check.</returns>
    [Fact]
    public async Task DownloadPublishesCopyWritesToHostBeforeMappingAsync()
    {
        IGraphicDevice device = DispatchProxy.Create<IGraphicDevice, DeviceProxy>();
        using var source = new TestBuffer(new() { Count = 4, Usage = BufferUsage.CopySource });
        using var run = new GraphicsRun(device, (GraphicsDeviceGeneration)typeof(GraphicsDeviceGeneration).GetMethod("CreateLocal", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [device])!, new(4096, 64, 64, 1), [0]);

        // Mapping-only fixture: cancellation prevents publishing a Core result.
        var context = new GraphicsStagePreparationContext(run, null!, 0, null, null!);
        using var cancellation = new CancellationTokenSource();
        Task<ValueLease<Host<Words>>> pending = new DeviceStorage<uint>(source.SliceElements(1, 2))
            .DownloadAsync<Words>(context, new(2, new Dictionary<string, object>()), cancellation.Token).AsTask();
        IOriginCommands commands = DispatchProxy.Create<IOriginCommands, CommandProxy>();
        var recorder = (CommandProxy)(object)commands;
        recorder.Device = device;
        TestBuffer readback = ((DeviceProxy)(object)device).Readback!;
        try
        {
            run.Record(commands);
            Assert.Equal(3, recorder.Events.Count);
            Assert.IsType<MemoryBarrierDesc>(recorder.Events[0]);
            Assert.Equal("copy", recorder.Events[1]);
            BufferBarrierDesc<uint> barrier = Assert.IsType<BufferBarrierDesc<uint>>(recorder.Events[2]);
            Assert.Same(readback, barrier.Buffer.Buffer);
            Assert.Equal(0UL, barrier.Buffer.Offset);
            Assert.Equal(2UL, barrier.Buffer.Count);
            Assert.Equal(new(PipelineStage.Copy, ResourceAccess.CopyWrite), barrier.Before);
            Assert.Equal(new(PipelineStage.Host, ResourceAccess.HostRead), barrier.After);
            Assert.False(readback.IsMapped);
            await run.MapEpochAsync(0);
            Assert.False(readback.IsMapped);
            Assert.Equal(1, readback.Maps);
        }
        finally
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }

        run.Dispose();
        Assert.True(readback.Disposed);
        Assert.Equal(0, run.ReservedWorkingBytes);
    }

    /// <summary>Rejects all unexpected device operations, including queue access.</summary>
    public class DeviceProxy : DispatchProxy
    {
        internal TestBuffer? Readback { get; private set; }

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "get_Status")
            {
                return GraphicsDeviceStatus.Ready;
            }

            if (targetMethod.Name == "CreateBuffer")
            {
                Readback = new((BufferDesc<uint>)args![0]!);
                return Readback;
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }

    /// <summary>Captures only the real transfer callback's commands.</summary>
    public class CommandProxy : DispatchProxy
    {
        internal IGraphicDevice Device { get; set; } = null!;

        internal List<object> Events { get; } = [];

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "get_Device": return Device;
                case "get_State": return CommandBufferState.Recording;
                case "ValidateOrigin": return null;
                case "Barrier": Events.Add(args![0]!); return null;
                case "CopyBuffer": Events.Add("copy"); return null;
                default: throw new NotSupportedException(targetMethod.Name);
            }
        }
    }

    internal sealed class TestBuffer(BufferDesc<uint> desc) : IGraphicsBuffer<uint>
    {
        public ulong SizeInBytes => Count * 4;

        public BufferLayout<uint> Layout => new(4, 4, 1, 1);

        public ulong Count => desc.Count;

        public BufferUsage Usage => desc.Usage;

        public MemoryPreference Memory => desc.Memory;

        public bool IsMapped { get; private set; }

        internal int Maps { get; private set; }

        internal bool Disposed { get; private set; }

        public void ValidateByteRange(ulong offset, ulong length) => Assert.True(offset <= SizeInBytes && length <= SizeInBytes - offset);

        public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length) => throw new NotSupportedException();

        public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => throw new NotSupportedException();

        public ValueTask MapAsync(CancellationToken cancellationToken = default)
        {
            Maps++;
            IsMapped = true;
            return ValueTask.CompletedTask;
        }

        public void Unmap() => IsMapped = false;

        public BufferSlice<uint> SliceElements(ulong offset, ulong count) => new(this, offset, count);

        public void CopyFrom(ReadOnlySpan<uint> source) => throw new NotSupportedException();

        public void CopyTo(Span<uint> destination)
        {
            Assert.True(IsMapped);
            destination.Clear();
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class Words : ISchema
    {
        public static SchemaDescriptor Descriptor { get; } = new(1, typeof(uint), 4, new Dictionary<string, object>());
    }
}
