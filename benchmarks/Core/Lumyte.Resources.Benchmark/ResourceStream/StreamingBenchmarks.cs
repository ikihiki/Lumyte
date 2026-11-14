using BenchmarkDotNet.Attributes;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Resources.Execution;

namespace Lumyte.Resources.Benchmark;

/// <summary>Consumes finite ordered streams through actual Execution dispatch.</summary>
[BenchmarkCategory("Core", "Resources", "Streaming", "Screening", "Comparison", "Diagnostic")]
public class StreamingBenchmarks
{
    private MemoryManager _memory = null!;
    private ExecutionRuntime _execution = null!;
    private ResourceManager _manager = null!;

    /// <summary>Gets or sets the finite element count.</summary>
    [Params(0, 4096, 65536)]
    public int Length { get; set; }

    /// <summary>Starts the runtime and registers the controlled stream factory.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _memory = new MemoryManager();
        var resources = new ResourceRuntime();
        _execution = new ExecutionRuntime([resources]);
        _execution.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
        _execution.StartAsync().GetAwaiter().GetResult();
        _manager = new ResourceManager("stream", new ResourcesBenchmarks.UnusedSource(), [new FactoryStage(Length)], _memory, resources);
    }

    /// <summary>Closes all resources before stopping execution and memory.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _execution.StopAsync().AsTask().GetAwaiter().GetResult();
        _memory.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Opens, fully consumes and closes a new finite cursor on each invocation.</summary>
    /// <returns>The ordered element checksum.</returns>
    [Benchmark]
    public async Task<long> ReadAllAsync()
    {
        await using ResourceStreamSession<int> session = await OpenAsync();
        int[] block = new int[256];
        long sum = 0;
        int count;
        while ((count = await session.ReadAsync(block)) != 0)
        {
            for (int index = 0; index < count; index++)
            {
                sum += block[index];
            }
        }

        return sum;
    }

    /// <summary>Seeks a fresh cursor and completes the next valid read.</summary>
    /// <returns>The first element after seeking, or minus one for an empty stream.</returns>
    [Benchmark]
    public async Task<int> SeekAndReadAsync()
    {
        await using ResourceStreamSession<int> session = await OpenAsync();
        await session.SeekAsync((ulong)(Length / 2));
        int[] value = new int[1];
        return await session.ReadAsync(value) == 0 ? -1 : value[0];
    }

    private ValueTask<ResourceStreamSession<int>> OpenAsync() => _manager.OpenStreamAsync<Factory, int>(new ResourceKey<Factory>("stream"), new ResourceStreamOptions { BlockSize = 256, MaxBufferedBlocks = 2 });

    private sealed class FactoryStage(int length) : IResourceSourceStage<Factory>
    {
        public ValueTask<Factory> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => ValueTask.FromResult(new Factory(length));

        public ValueTask ReleaseAsync(Factory resource) => ValueTask.CompletedTask;
    }

    private sealed class Factory(int length) : IResourceStream<int>
    {
        public ValueTask<IResourceStreamReader<int>> OpenReaderAsync(ResourceStreamOptions options, IMemoryScope memory, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IResourceStreamReader<int>>(new Reader(length));
    }

    private sealed class Reader(int length) : IResourceStreamReader<int>
    {
        private int _position;

        public bool CanSeek => true;

        public ValueTask<int> ReadAsync(Memory<int> destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(destination.Length, length - _position);
            for (int index = 0; index < count; index++)
            {
                destination.Span[index] = _position++;
            }

            return ValueTask.FromResult(count);
        }

        public ValueTask SeekAsync(ulong elementOffset, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _position = checked((int)elementOffset);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
