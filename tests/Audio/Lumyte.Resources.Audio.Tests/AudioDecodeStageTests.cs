using System.Buffers.Binary;
using System.Diagnostics;

namespace Lumyte.Resources.Audio.Tests;

/// <summary>Checks source-length hints while the default runtime schedules the parameterless decode stage.</summary>
public sealed class AudioDecodeStageTests
{
    /// <summary>Decode resumes on the scheduler after I/O, and source lengths only hint bounded preallocation.</summary>
    /// <returns>The completed decode and cleanup check.</returns>
    [Fact]
    public async Task LengthOnlyHintsBoundedInitialCapacityAsync()
    {
        byte[] wave = CreateWave();
        var source = new ObservedSource(wave);
        var runtime = new ResourceRuntime();
        var execution = new ExecutionRuntime([runtime]);
        execution.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        var memory = new MemoryManager();
        var manager = new ResourceManager("decode", source, [new AudioDecodeStage()], memory, runtime);
        try
        {
            Wait(execution, execution.StartAsync().AsTask());
            long?[] lengths = [wave.Length, null, 0, 1, long.MaxValue];
            for (int i = 0; i < lengths.Length; i++)
            {
                source.Length = lengths[i];
                Task<ResourceReference<AudioPcmData>> load = manager.AcquireAsync(new ResourceKey<AudioPcmData>($"wave/{i}")).AsTask();
                Wait(execution, load);
                using ResourceReference<AudioPcmData> reference = await load;
                using ResourceLease<AudioPcmData> lease = reference.Acquire();
                Assert.Equal(new float[] { -1, 0, 0.5f, 0 }, lease.Value.Samples.ToArray());
                Assert.InRange(source.InitialCapacity, 0, 1024 * 1024);
                if (lengths[i] == wave.Length)
                {
                    Assert.Equal(wave.Length, source.InitialCapacity);
                }
            }
        }
        finally
        {
            Wait(execution, manager.DisposeAsync().AsTask());
            Wait(execution, execution.StopAsync().AsTask());
            await memory.DisposeAsync();
        }
    }

    private static void Wait(ExecutionRuntime execution, Task task)
    {
        long start = Stopwatch.GetTimestamp();
        while (!task.IsCompleted)
        {
            execution.PumpHostLifecycle(16, TimeSpan.FromMilliseconds(2));
            execution.PumpJobs(16, TimeSpan.FromMilliseconds(2));
            if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("Audio decode did not complete.");
            }

            Thread.Sleep(1);
        }

        task.GetAwaiter().GetResult();
    }

    private static byte[] CreateWave()
    {
        byte[] bytes = new byte[52];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length - 8);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 48000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 48000 * 4);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 8);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44), short.MinValue);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(48), 16384);
        return bytes;
    }

    private sealed class ObservedSource(byte[] bytes) : IResourceSource
    {
        internal long? Length { get; set; }

        internal int InitialCapacity { get; set; }

        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ResourceSourceSnapshot>(new Snapshot(id, this, bytes));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>("1");
    }

    private sealed class Snapshot(ResourceId id, ObservedSource source, byte[] bytes) : ResourceSourceSnapshot
    {
        public override ResourceId Id => id;

        public override string Revision => "1";

        public override long? Length => source.Length;

        public override bool CanReadRange => false;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(new ObservedStream(source, bytes));

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ObservedStream(ObservedSource source, byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        private SynchronizationContext? _workflowContext;
        private int _thread;

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            _workflowContext = SynchronizationContext.Current;
            _thread = Environment.CurrentManagedThreadId;
            source.InitialCapacity = Assert.IsType<MemoryStream>(destination).Capacity;
            await Task.Yield();
            await base.CopyToAsync(destination, bufferSize, cancellationToken);
        }

        public override ValueTask DisposeAsync()
        {
            // AudioDecodeStage disposes the stream after decoding, so this observes its post-I/O continuation.
            Assert.NotNull(_workflowContext);
            Assert.Same(_workflowContext, SynchronizationContext.Current);
            Assert.Equal(_thread, Environment.CurrentManagedThreadId);
            return base.DisposeAsync();
        }
    }
}
