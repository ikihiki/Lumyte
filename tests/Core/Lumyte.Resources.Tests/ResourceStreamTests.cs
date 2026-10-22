using Lumyte.Memory;
using Lumyte.Memory.DependencyInjection;
using Lumyte.Resources.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Tests bounded streaming, independent cursors and retryable session ownership.</summary>
public sealed class ResourceStreamTests
{
    private static readonly ResourceKey<TestFactory> _key = new("audio/track");
    private static readonly MemoryPoolId _cpu = new("cpu");
    private static readonly ResourceStreamOptions _options = new() { BlockSize = 4, MaxBufferedBlocks = 2 };

    /// <summary>Each session has its own cursor and prefetching stops at the configured buffer bound.</summary>
    /// <returns>Completion of the independent-reader checks.</returns>
    [Fact]
    public async Task IndependentSessionsHaveBoundedPrefetchAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings());
        await using ResourceStreamSession<int> first = await harness.OpenAsync();
        await using ResourceStreamSession<int> second = await harness.OpenAsync();
        Assert.Equal(2, harness.Settings.ReadersOpened);
        Assert.Equal(16, harness.Settings.ElementsRead);
        Assert.Equal(0UL, first.Position);
        Assert.Equal(0UL, second.Position);
        Assert.Equal(first.Revision, second.Revision);

        int[] values = new int[4];
        Assert.Equal(4, first.ReadAvailable(values));
        Assert.Equal(new[] { 0, 1, 2, 3 }, values);
        Assert.Equal(4UL, first.Position);
        Assert.Equal(0UL, second.Position);
        Assert.Equal(4, second.ReadAvailable(values));
        Assert.Equal(new[] { 0, 1, 2, 3 }, values);
        Assert.True(harness.Memory.GetSnapshot(_cpu).CommittedBytes > 0);
    }

    /// <summary>Reload changes new sessions while an existing reader keeps its old revision, input and cursor.</summary>
    /// <returns>Completion of the old and new streaming generation checks.</returns>
    [Fact]
    public async Task ReloadPreservesExistingSessionAndUpdatesNewSessionsAsync()
    {
        var settings = new StreamSettings();
        await using var harness = new StreamHarness(settings);
        await using ResourceStreamSession<int> original = await harness.OpenAsync();
        ulong originalRevision = original.Revision;
        int[] values = new int[4];
        Assert.Equal(4, original.ReadAvailable(values));
        Assert.Equal(new[] { 0, 1, 2, 3 }, values);

        settings.SourceVersion = 2;
        settings.BaseValue = 1000;
        ResourceReloadResult result = await harness.Manager.ReloadForTestAsync([_key.Address.Id]);
        Assert.Equal(ResourceReloadStatus.Published, result.Status);
        await using ResourceStreamSession<int> current = await harness.OpenAsync();
        Assert.NotEqual(originalRevision, current.Revision);
        Assert.Equal(originalRevision, original.Revision);
        Assert.Equal(4UL, original.Position);
        Assert.Equal(0UL, current.Position);
        Assert.Equal(4, current.ReadAvailable(values));
        Assert.Equal(new[] { 1000, 1001, 1002, 1003 }, values);

        int expected = 4;
        while (expected < 24)
        {
            int count = await original.ReadAsync(values);
            Assert.True(count > 0);
            for (int index = 0; index < count; index++)
            {
                Assert.Equal(expected++, values[index]);
            }
        }

        Assert.Equal(24UL, original.Position);
        Assert.Equal(4UL, current.Position);
        await harness.Manager.TrimAsync();
        Assert.Equal(0, settings.SnapshotsDisposed);
        await original.DisposeAsync();
        await harness.Manager.TrimAsync();
        Assert.Equal(1, settings.SnapshotsDisposed);
        Assert.Equal(4, await current.ReadAsync(values));
        Assert.Equal(new[] { 1004, 1005, 1006, 1007 }, values);
    }

    /// <summary>Short reads preserve ordering and distinguish buffered underflow from the end of input.</summary>
    /// <returns>Completion of the sequential-read checks.</returns>
    [Fact]
    public async Task ShortReadsAndEndOfStreamPreserveSequenceAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings { Length = 11, ReadLimit = 3 });
        await using ResourceStreamSession<int> session = await harness.OpenAsync();
        var received = new List<int>();
        int[] block = new int[5];
        int count;
        while ((count = await session.ReadAsync(block)) != 0)
        {
            received.AddRange(block.AsSpan(0, count).ToArray());
        }

        Assert.Equal(Enumerable.Range(0, 11), received);
        Assert.Equal(11UL, session.Position);
        Assert.True(session.EndOfStream);
        Assert.Equal(0, session.ReadAvailable(block));
    }

    /// <summary>Repeated ring wraparound preserves all elements without growing the buffer budget.</summary>
    /// <returns>Completion of the sustained read checks.</returns>
    [Fact]
    public async Task LongStreamReusesFixedBufferBudgetAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings { Length = 8192, ReadLimit = 7 });
        var options = new ResourceStreamOptions { BlockSize = 17, MaxBufferedBlocks = 3 };
        await using ResourceStreamSession<int> session = await harness.OpenAsync(options);
        ulong budget = harness.Memory.GetSnapshot(_cpu).CommittedBytes;
        int[] values = new int[53];
        int expected = 0;
        int count;
        while ((count = await session.ReadAsync(values)) != 0)
        {
            for (int index = 0; index < count; index++)
            {
                Assert.Equal(expected++, values[index]);
            }

            Assert.Equal(budget, harness.Memory.GetSnapshot(_cpu).CommittedBytes);
        }

        Assert.Equal(8192, expected);
    }

    /// <summary>A cancelled consumer wait does not stop the producer or consume any elements.</summary>
    /// <returns>Completion of the read cancellation checks.</returns>
    [Fact]
    public async Task ReadCancellationOnlyCancelsItsWaitAsync()
    {
        var settings = new StreamSettings { PauseAt = 8 };
        await using var harness = new StreamHarness(settings);
        await using ResourceStreamSession<int> session = await harness.OpenAsync();
        int[] buffered = new int[8];
        Assert.Equal(8, session.ReadAvailable(buffered));
        Assert.Equal(0, session.ReadAvailable(buffered));
        Assert.False(session.EndOfStream);

        using var cancellation = new CancellationTokenSource();
        Task<int> waiting = session.ReadAsync(buffered, cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(8UL, session.Position);
        settings.Continue.TrySetResult();
        Assert.True(await session.ReadAsync(buffered) > 0);
        Assert.Equal(8, buffered[0]);
    }

    /// <summary>The callback path uses only preallocated storage and performs no reader calls.</summary>
    /// <returns>Completion of the callback checks.</returns>
    [Fact]
    public async Task ReadAvailableDoesNotAllocateOrReadInputAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings());
        var options = new ResourceStreamOptions { BlockSize = 16, MaxBufferedBlocks = 1 };
        await using ResourceStreamSession<int> session = await harness.OpenAsync(options);
        int[] destination = new int[1];
        Assert.Equal(1, session.ReadAvailable(destination));
        int calls = harness.Settings.ReadCalls;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 8; index++)
        {
            session.ReadAvailable(destination);
        }

        long difference = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(0L, difference);
        Assert.Equal(calls, harness.Settings.ReadCalls);
        Assert.Equal(9UL, session.Position);
    }

    /// <summary>Seeking discards prefetched elements and resets the public position independently.</summary>
    /// <returns>Completion of the seek checks.</returns>
    [Fact]
    public async Task SeekReplacesPrefetchedDataAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings());
        await using ResourceStreamSession<int> session = await harness.OpenAsync();
        await session.SeekAsync(19);
        Assert.Equal(19UL, session.Position);
        int[] values = new int[4];
        Assert.Equal(4, session.ReadAvailable(values));
        Assert.Equal(new[] { 19, 20, 21, 22 }, values);
        Assert.Equal(23UL, session.Position);
        Assert.Equal(1, harness.Settings.Seeks);
    }

    /// <summary>Seeking cancels an in-flight producer read before moving the reader's cursor.</summary>
    /// <returns>Completion of the producer coordination checks.</returns>
    [Fact]
    public async Task SeekWaitsForBlockedProducerToStopAsync()
    {
        var settings = new StreamSettings { PauseAt = 8 };
        await using var harness = new StreamHarness(settings);
        await using ResourceStreamSession<int> session = await harness.OpenAsync();
        Assert.Equal(4, session.ReadAvailable(new int[4]));
        await settings.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await session.SeekAsync(19);
        Assert.True(settings.PauseExited.Task.IsCompleted);
        int[] values = new int[4];
        Assert.Equal(4, session.ReadAvailable(values));
        Assert.Equal(new[] { 19, 20, 21, 22 }, values);
    }

    /// <summary>An unsuccessful seek faults later reads while retaining cleanup ownership.</summary>
    /// <returns>Completion of the fault checks.</returns>
    [Fact]
    public async Task FailedSeekFaultsSessionAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings { FailSeek = true });
        await using ResourceStreamSession<int> session = await harness.OpenAsync();
        await Assert.ThrowsAsync<IOException>(() => session.SeekAsync(8).AsTask());
        Assert.Throws<IOException>(() => session.ReadAvailable(new int[1]));
    }

    /// <summary>A reader error faults only its session and propagates the original error.</summary>
    /// <returns>Completion of the producer fault checks.</returns>
    [Fact]
    public async Task ProducerFailureIsObservableAsync()
    {
        var settings = new StreamSettings { FailReadAt = 8 };
        await using var harness = new StreamHarness(settings);
        await using ResourceStreamSession<int> session = await harness.OpenAsync();
        Assert.Equal(8, session.ReadAvailable(new int[8]));
        IOException exception = await Assert.ThrowsAsync<IOException>(() => session.ReadAsync(new int[1]).AsTask());
        Assert.Equal("read failed", exception.Message);
    }

    /// <summary>A failed reader disposal retains both the memory budget and the factory lease until retry.</summary>
    /// <returns>Completion of the cleanup retry checks.</returns>
    [Fact]
    public async Task FailedDisposalRetainsBuffersUntilRetryAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings { DisposeFailures = 1 });
        ResourceStreamSession<int> session = await harness.OpenAsync();
        ulong before = harness.Memory.GetSnapshot(_cpu).CommittedBytes;
        await Assert.ThrowsAsync<IOException>(() => session.DisposeAsync().AsTask());
        Assert.Equal(before, harness.Memory.GetSnapshot(_cpu).CommittedBytes);
        Assert.Throws<ObjectDisposedException>(() => session.ReadAvailable(new int[1]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Manager.DisposeAsync().AsTask());
        await session.DisposeAsync();
        Assert.Equal(0UL, harness.Memory.GetSnapshot(_cpu).CommittedBytes);
        Assert.Equal(1, harness.Settings.ReadersDisposed);
    }

    /// <summary>Concurrent disposal calls share the same task and release the reader only once.</summary>
    /// <returns>Completion of the concurrent disposal checks.</returns>
    [Fact]
    public async Task ConcurrentDisposalSharesCompletionAsync()
    {
        var settings = new StreamSettings { DisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var harness = new StreamHarness(settings);
        ResourceStreamSession<int> session = await harness.OpenAsync();
        Task first = session.DisposeAsync().AsTask();
        Task second = session.DisposeAsync().AsTask();
        Assert.Same(first, second);
        Assert.False(first.IsCompleted);
        settings.DisposeGate.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, settings.ReadersDisposed);
    }

    /// <summary>A token supplied for opening cannot cancel a session after it has been returned.</summary>
    /// <returns>Completion of the creation token checks.</returns>
    [Fact]
    public async Task CreationCancellationDoesNotBecomeSessionCancellationAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings());
        using var cancellation = new CancellationTokenSource();
        await using ResourceStreamSession<int> session = await harness.OpenAsync(cancellationToken: cancellation.Token);
        cancellation.Cancel();
        int[] values = new int[8];
        Assert.Equal(8, session.ReadAvailable(values));
        Assert.True(await session.ReadAsync(values) > 0);
        Assert.Equal(8, values[0]);
    }

    /// <summary>Cancellation while waiting for the first block closes the reader and returns its buffers.</summary>
    /// <returns>Completion of the interrupted initialization checks.</returns>
    [Fact]
    public async Task CancelledCreationReleasesSessionOwnershipAsync()
    {
        var settings = new StreamSettings { PauseAt = 0 };
        await using var harness = new StreamHarness(settings);
        using var cancellation = new CancellationTokenSource();
        Task<ResourceStreamSession<int>> opening = harness.OpenAsync(cancellationToken: cancellation.Token).AsTask();
        await settings.Opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        Assert.Equal(1, settings.ReadersDisposed);
        Assert.Equal(0UL, harness.Memory.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>A factory failure releases buffers even when it never returns a reader.</summary>
    /// <returns>Completion of the failed initialization checks.</returns>
    [Fact]
    public async Task FailedReaderCreationReleasesAllocatedBuffersAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings { FailOpen = true });
        await Assert.ThrowsAsync<IOException>(() => harness.OpenAsync().AsTask());
        Assert.Equal(0, harness.Settings.ReadersDisposed);
        Assert.Equal(0UL, harness.Memory.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>An empty reader completes opening without allocating additional blocks or inventing data.</summary>
    /// <returns>Completion of the empty stream checks.</returns>
    [Fact]
    public async Task EmptyStreamIsAtEndImmediatelyAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings { Length = 0 });
        await using ResourceStreamSession<int> session = await harness.OpenAsync();
        Assert.True(session.EndOfStream);
        Assert.Equal(0UL, session.Position);
        Assert.Equal(0, await session.ReadAsync(new int[4]));
    }

    /// <summary>Failed initialization cleanup remains owned by the manager until a later trim succeeds.</summary>
    /// <returns>Completion of the abandoned session retry checks.</returns>
    [Fact]
    public async Task ManagerRetainsFailedInitializationCleanupAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings { FailReadAt = 0, DisposeFailures = 1 });
        AggregateException error = await Assert.ThrowsAsync<AggregateException>(() => harness.OpenAsync().AsTask());
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.True(harness.Memory.GetSnapshot(_cpu).CommittedBytes > 0);
        Assert.Equal(0, harness.Settings.ReadersDisposed);
        await harness.Manager.TrimAsync();
        Assert.Equal(0UL, harness.Memory.GetSnapshot(_cpu).CommittedBytes);
        Assert.Equal(1, harness.Settings.ReadersDisposed);
    }

    /// <summary>A nonseekable stream rejects seeking while preserving its buffered data.</summary>
    /// <returns>Completion of the nonseekable checks.</returns>
    [Fact]
    public async Task UnsupportedSeekPreservesSessionAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings { CanSeek = false });
        await using ResourceStreamSession<int> session = await harness.OpenAsync();
        Assert.False(session.CanSeek);
        await Assert.ThrowsAsync<NotSupportedException>(() => session.SeekAsync(8).AsTask());
        int[] values = new int[4];
        Assert.Equal(4, session.ReadAvailable(values));
        Assert.Equal(new[] { 0, 1, 2, 3 }, values);
    }

    /// <summary>Invalid buffering limits are rejected before a reader or memory scope is acquired.</summary>
    /// <returns>Completion of the options checks.</returns>
    [Fact]
    public async Task InvalidOptionsAreRejectedAsync()
    {
        await using var harness = new StreamHarness(new StreamSettings());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => harness.OpenAsync(new ResourceStreamOptions { BlockSize = 0 }).AsTask());
        await Assert.ThrowsAsync<OverflowException>(() => harness.OpenAsync(new ResourceStreamOptions { BlockSize = int.MaxValue, MaxBufferedBlocks = 2 }).AsTask());
        Assert.Equal(0, harness.Settings.ReadersOpened);
        Assert.Equal(0UL, harness.Memory.GetSnapshot(_cpu).CommittedBytes);
    }

    private sealed class StreamHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        public StreamHarness(StreamSettings settings)
        {
            Settings = settings;
            var services = new ServiceCollection();
            services.AddSingleton(settings);
            services.AddMemoryManagement(options => options.ChunkSizeBytes = 32);
            services.AddResources("streaming", builder => builder.UseSource<TestSource>().AddSourcePipeline<TestFactory, TestStage>());
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            _scope = _provider.CreateAsyncScope();
            Manager = _scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("streaming");
            Memory = _scope.ServiceProvider.GetRequiredService<IMemoryManager>();
        }

        public StreamSettings Settings { get; }

        public ResourceManager Manager { get; }

        public IMemoryManager Memory { get; }

        public ValueTask<ResourceStreamSession<int>> OpenAsync(ResourceStreamOptions? options = null, CancellationToken cancellationToken = default) =>
            Manager.OpenStreamAsync<TestFactory, int>(_key, options ?? _options, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    private sealed class StreamSettings
    {
        private int _disposeFailures;
        private int _readCalls;
        private int _elementsRead;
        private int _readersOpened;
        private int _readersDisposed;
        private int _seeks;
        private int _snapshotsDisposed;

        public int SourceVersion { get; set; } = 1;

        public int BaseValue { get; set; }

        public int Length { get; init; } = 32;

        public int ReadLimit { get; init; } = int.MaxValue;

        public int PauseAt { get; init; } = int.MaxValue;

        public int FailReadAt { get; init; } = int.MaxValue;

        public bool FailSeek { get; init; }

        public bool FailOpen { get; init; }

        public bool CanSeek { get; init; } = true;

        public int DisposeFailures
        {
            get => Volatile.Read(ref _disposeFailures);
            init => _disposeFailures = value;
        }

        public int ReadCalls => Volatile.Read(ref _readCalls);

        public int ElementsRead => Volatile.Read(ref _elementsRead);

        public int ReadersOpened => Volatile.Read(ref _readersOpened);

        public int ReadersDisposed => Volatile.Read(ref _readersDisposed);

        public int Seeks => Volatile.Read(ref _seeks);

        public int SnapshotsDisposed => Volatile.Read(ref _snapshotsDisposed);

        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PauseExited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource? DisposeGate { get; init; }

        public void ReadStarted() => Interlocked.Increment(ref _readCalls);

        public void ElementsProduced(int count) => Interlocked.Add(ref _elementsRead, count);

        public void ReaderOpened() => Interlocked.Increment(ref _readersOpened);

        public void ReaderDisposed() => Interlocked.Increment(ref _readersDisposed);

        public void SeekCompleted() => Interlocked.Increment(ref _seeks);

        public void SnapshotDisposed() => Interlocked.Increment(ref _snapshotsDisposed);

        public bool FailDisposal() => Interlocked.Decrement(ref _disposeFailures) >= 0;
    }

    private sealed class TestStage(StreamSettings settings) : IResourceSourceStage<TestFactory>
    {
        public async ValueTask<TestFactory> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            ResourceSourceSnapshot snapshot = await context.OpenSourceAsync(context.Address.Id, ResourceDependencyLifetime.Resident, cancellationToken);
            return new TestFactory(settings, ((TestSnapshot)snapshot).BaseValue);
        }

        public ValueTask ReleaseAsync(TestFactory resource) => ValueTask.CompletedTask;
    }

    private sealed class TestSource(StreamSettings settings) : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) =>
            new(new TestSnapshot(id, settings.SourceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), settings.BaseValue, settings));

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) =>
            new(settings.SourceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class TestSnapshot(ResourceId id, string revision, int baseValue, StreamSettings settings) : ResourceSourceSnapshot
    {
        private bool _disposed;

        public override ResourceId Id => id;

        public override string Revision => revision;

        public override long? Length => sizeof(int);

        public override bool CanReadRange => false;

        public int BaseValue => baseValue;

        public override ValueTask<Stream> OpenReadAsync(long offset = 0, long? length = null, CancellationToken cancellationToken = default) =>
            new(new MemoryStream(BitConverter.GetBytes(baseValue), writable: false));

        public override ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                settings.SnapshotDisposed();
                _disposed = true;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestFactory(StreamSettings settings, int baseValue) : IResourceStream<int>
    {
        public ValueTask<IResourceStreamReader<int>> OpenReaderAsync(ResourceStreamOptions options, IMemoryScope memory, CancellationToken cancellationToken = default)
        {
            if (settings.FailOpen)
            {
                throw new IOException("open failed");
            }

            settings.ReaderOpened();
            settings.Opened.TrySetResult();
            return new(new TestReader(settings, baseValue));
        }
    }

    private sealed class TestReader(StreamSettings settings, int baseValue) : IResourceStreamReader<int>
    {
        private int _position;
        private bool _hasSought;

        public bool CanSeek => settings.CanSeek;

        public async ValueTask<int> ReadAsync(Memory<int> destination, CancellationToken cancellationToken = default)
        {
            settings.ReadStarted();
            if (_position >= settings.PauseAt && !_hasSought)
            {
                settings.Paused.TrySetResult();
                try
                {
                    await settings.Continue.Task.WaitAsync(cancellationToken);
                }
                finally
                {
                    settings.PauseExited.TrySetResult();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (_position >= settings.FailReadAt)
            {
                throw new IOException("read failed");
            }

            int count = Math.Min(Math.Min(destination.Length, settings.ReadLimit), settings.Length - _position);
            for (int index = 0; index < count; index++)
            {
                destination.Span[index] = baseValue + _position++;
            }

            settings.ElementsProduced(count);
            return count;
        }

        public ValueTask SeekAsync(ulong elementOffset, CancellationToken cancellationToken = default)
        {
            if (settings.FailSeek)
            {
                throw new IOException("seek failed");
            }

            _position = checked((int)elementOffset);
            _hasSought = true;
            settings.SeekCompleted();
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (settings.DisposeGate is not null)
            {
                await settings.DisposeGate.Task;
            }

            if (settings.FailDisposal())
            {
                throw new IOException("dispose failed");
            }

            settings.ReaderDisposed();
        }
    }
}
