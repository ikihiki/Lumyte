using BenchmarkDotNet.Attributes;
using Lumyte.Memory;

namespace Lumyte.Resources.Benchmark.Streaming;

/// <summary>Consumes finite ordered streams with a controlled ThreadPool executor, without Execution integration.</summary>
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class ResourceStreamBenchmarks
{
    private MemoryManager _memory = null!;
    private ResourceManager _manager = null!;
    private int _readersOpened;
    private int _readersClosed;

    /// <summary>Gets the number of real readers opened.</summary>
    public int ReadersOpened => _readersOpened;

    /// <summary>Gets the number of real readers closed.</summary>
    public int ReadersClosed => _readersClosed;

    /// <summary>Gets or sets the finite element count.</summary>
    [Params(0, 4096, 65536)]
    public int Length { get; set; }

    /// <summary>Registers the controlled stream factory without a product execution runtime.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _readersOpened = 0;
        _readersClosed = 0;
        _memory = new MemoryManager();
        _manager = new ResourceManager("stream", new ResourcesBenchmarks.UnusedSource(), [new FactoryStage(Length, this)], _memory, new StreamRuntime());
    }

    /// <summary>Closes all stream resources before disposing memory.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

    private sealed class StreamRuntime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
            => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource)
            => stage.ReleaseAsync(resource, runtime);

        public ValueTask StreamAsync(IResourceStreamWork work, CancellationToken cancellationToken)
            => new(Task.Run(async () => await work.ExecuteAsync(cancellationToken), cancellationToken));
    }

    private sealed class FactoryStage(int length, ResourceStreamBenchmarks owner) : IResourceSourceStage<Factory>
    {
        public ValueTask<Factory> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => ValueTask.FromResult(new Factory(length, owner));

        public ValueTask ReleaseAsync(Factory resource) => ValueTask.CompletedTask;
    }

    private sealed class Factory(int length, ResourceStreamBenchmarks owner) : IResourceStream<int>
    {
        public ValueTask<IResourceStreamReader<int>> OpenReaderAsync(ResourceStreamOptions options, IMemoryScope memory, CancellationToken cancellationToken = default)
        {
            owner._readersOpened++;
            return ValueTask.FromResult<IResourceStreamReader<int>>(new Reader(length, owner));
        }
    }

    private sealed class Reader(int length, ResourceStreamBenchmarks owner) : IResourceStreamReader<int>
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

        public ValueTask DisposeAsync()
        {
            owner._readersClosed++;
            return ValueTask.CompletedTask;
        }
    }
}
