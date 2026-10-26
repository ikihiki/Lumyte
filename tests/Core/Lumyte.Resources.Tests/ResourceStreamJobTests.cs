using System.Collections.Concurrent;
using System.Diagnostics;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Resources.Execution;
using Xunit;

namespace Lumyte.Resources.Tests;

/// <summary>Verifies that streaming uses the configured CPU jobs and cooperative host.</summary>
public sealed class ResourceStreamJobTests
{
    private static readonly ResourceKey<Factory> _key = new("stream");
    private static readonly ResourceStreamOptions _options = new() { BlockSize = 4, MaxBufferedBlocks = 1 };

    /// <summary>Opening, refilling, seeking and reader cleanup only execute through pumped jobs.</summary>
    [Fact]
    public void CooperativeReaderLifecycleRequiresPumping()
    {
        using Host host = new();
        using ResourceReference<Factory> reference = host.Complete(host.Manager.AcquireAsync(_key).AsTask());
        Task<ResourceStreamSession<int>> opening = host.OpenAsync();
        Assert.Equal(0, host.State.Opened);
        Assert.False(opening.IsCompleted);
        ResourceStreamSession<int> session = host.Track(host.Complete(opening));
        int[] values = new int[4];
        Assert.Equal(4, session.ReadAvailable(values));
        int reads = host.State.Reads;
        Thread.Sleep(20);
        Assert.Equal(reads, host.State.Reads);
        host.Until(() => host.State.Reads > reads);
        Assert.Equal(4, session.ReadAvailable(values));
        Assert.Equal(new[] { 4, 5, 6, 7 }, values);

        Task seeking = session.SeekAsync(20).AsTask();
        Assert.Equal(0, host.State.Seeks);
        host.Complete(seeking);
        Assert.Equal(1, host.State.Seeks);
        Assert.Equal(4, session.ReadAvailable(values));
        Assert.Equal(new[] { 20, 21, 22, 23 }, values);

        Task disposal = session.DisposeAsync().AsTask();
        Assert.Equal(0, host.State.Disposed);
        host.Complete(disposal);
        Assert.Equal(1, host.State.Disposed);
        Assert.All(host.State.Calls, call => Assert.NotNull(call.Context));
        Assert.All(host.State.Calls, call => Assert.Equal(Environment.CurrentManagedThreadId, call.Thread));
    }

    /// <summary>An external I/O completion queues the decoder continuation for the host to pump.</summary>
    [Fact]
    public void AsynchronousReaderContinuationRequiresPumping()
    {
        var state = new State { ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using Host host = new(state: state);
        Task<ResourceStreamSession<int>> opening = host.OpenAsync();
        host.Until(() => state.Reads == 1);
        Assert.False(opening.IsCompleted);
        state.ReadGate.SetResult();
        Thread.Sleep(20);
        Assert.Equal(0, state.Decoded);
        ResourceStreamSession<int> session = host.Track(host.Complete(opening));
        Assert.Equal(1, state.Decoded);
        Assert.Equal(4, session.ReadAvailable(new int[4]));
        Assert.All(state.Calls, call => Assert.NotNull(call.Context));
        Assert.All(state.Calls, call => Assert.Equal(Environment.CurrentManagedThreadId, call.Thread));
    }

    /// <summary>A full stream buffer suspends its job without occupying the single CPU execution slot.</summary>
    [Fact]
    public void FullProducerLeavesCpuAvailableForAnotherResource()
    {
        using Host host = new();
        _ = host.Track(host.Complete(host.OpenAsync()));
        int reads = host.State.Reads;
        using ResourceReference<Factory> other = host.Complete(host.Manager.AcquireAsync(new ResourceKey<Factory>("other")).AsTask());
        Assert.Equal(2, host.State.Builds);
        Assert.Equal(reads, host.State.Reads);
    }

    /// <summary>Slow synchronous readers yield between blocks instead of filling every block in one host turn.</summary>
    [Fact]
    public void SlowReaderYieldsBetweenBlocks()
    {
        using Host host = new(state: new State { DecodeMilliseconds = 3 });
        Task<ResourceStreamSession<int>> opening = host.OpenAsync(new ResourceStreamOptions { BlockSize = 4, MaxBufferedBlocks = 8 });
        host.Until(() => host.State.Reads != 0);
        Assert.Equal(1, host.State.Reads);
        _ = host.Track(host.Complete(opening));
    }

    /// <summary>Seek and cleanup observe the old producer before reserving the only workflow slot again.</summary>
    [Fact]
    public void SingleAdmissionSlotSupportsSeekAndDisposal()
    {
        using Host host = new(capacity: 1);
        using ResourceReference<Factory> reference = host.Complete(host.Manager.AcquireAsync(_key).AsTask());
        ResourceStreamSession<int> session = host.Track(host.Complete(host.OpenAsync()));
        host.Complete(session.SeekAsync(12).AsTask());
        Assert.Equal(12UL, session.Position);
        host.Complete(session.DisposeAsync().AsTask());
        Assert.Equal(1, host.State.Seeks);
        Assert.Equal(1, host.State.Disposed);
    }

    /// <summary>A refused producer completes opening with the admission error and releases its ownership.</summary>
    [Fact]
    public void AdmissionFailureDoesNotHangOrRetainAnUnopenedSession()
    {
        using Host host = new(capacity: 1);
        using ResourceReference<Factory> reference = host.Complete(host.Manager.AcquireAsync(_key).AsTask());
        ResourceStreamSession<int> first = host.Track(host.Complete(host.OpenAsync()));
        Task<ResourceStreamSession<int>> rejected = host.OpenAsync();
        host.Until(() => rejected.IsCompleted);
        Assert.Throws<ExecutionQueueFullException>(() => rejected.GetAwaiter().GetResult());
        Assert.Equal(1, host.State.Opened);
        host.Complete(first.DisposeAsync().AsTask());
        reference.Dispose();
        host.Complete(host.Manager.TrimAsync().AsTask());
        host.Complete(host.Manager.DisposeAsync().AsTask());
    }

    /// <summary>Canceling a queued producer returns its buffer, lease and only admission without opening a reader.</summary>
    [Fact]
    public void CancelingQueuedOpenReturnsOwnershipAndAdmission()
    {
        using Host host = new(capacity: 1);
        using ResourceReference<Factory> reference = host.Complete(host.Manager.AcquireAsync(_key).AsTask());
        ulong baseline = host.CommittedBytes;
        using var cancellation = new CancellationTokenSource();
        Task<ResourceStreamSession<int>> opening = host.OpenAsync(cancellationToken: cancellation.Token);
        Assert.Equal(1, host.Default.Streams);
        Assert.Equal(0, host.State.Opened);
        Assert.False(opening.IsCompleted);
        cancellation.Cancel();
        host.Until(() => opening.IsCompleted);
        Assert.ThrowsAny<OperationCanceledException>(() => opening.GetAwaiter().GetResult());
        Assert.Equal(0, host.State.Opened);
        Assert.Equal(0, host.State.Disposed);
        Assert.Equal(baseline, host.CommittedBytes);

        ResourceStreamSession<int> replacement = host.Track(host.Complete(host.OpenAsync()));
        host.Complete(replacement.DisposeAsync().AsTask());
        reference.Dispose();
        Assert.Equal(1, host.Complete(host.Manager.TrimAsync().AsTask()));
        host.Complete(host.Manager.DisposeAsync().AsTask());
    }

    /// <summary>Canceling post-seek prefetch faults the session and joins the job before admitting replacement work.</summary>
    [Fact]
    public void CancelingSeekJoinsProducerAndFaultsReads()
    {
        var state = new State { SeekReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using Host host = new(capacity: 1, state: state);
        using ResourceReference<Factory> reference = host.Complete(host.Manager.AcquireAsync(_key).AsTask());
        ResourceStreamSession<int> session = host.Track(host.Complete(host.OpenAsync()));
        using var cancellation = new CancellationTokenSource();
        Task seeking = session.SeekAsync(12, cancellation.Token).AsTask();
        host.Until(() => state.Seeks == 1 && state.Reads == 2);
        Assert.False(seeking.IsCompleted);
        cancellation.Cancel();
        Thread.Sleep(20);
        Assert.False(seeking.IsCompleted);
        host.Until(() => seeking.IsCompleted);
        Assert.ThrowsAny<OperationCanceledException>(() => seeking.GetAwaiter().GetResult());
        Assert.ThrowsAny<OperationCanceledException>(() => session.ReadAvailable(new int[4]));
        host.Complete(session.DisposeAsync().AsTask());
        Assert.Equal(1, state.Disposed);

        ResourceStreamSession<int> replacement = host.Track(host.Complete(host.OpenAsync()));
        Assert.Equal(2, state.Opened);
        Assert.Equal(4, replacement.ReadAvailable(new int[4]));
    }

    /// <summary>The factory's configured runtime controls all session jobs, including cleanup.</summary>
    [Fact]
    public void FactoryRuntimeSelectsStreamingExecutor()
    {
        using Host host = new(useOverride: true);
        ResourceStreamSession<int> session = host.Track(host.Complete(host.OpenAsync()));
        Assert.Equal(0, host.Default.Streams);
        Assert.Equal(1, host.Override.Streams);
        host.Complete(session.SeekAsync(12).AsTask());
        host.Complete(session.DisposeAsync().AsTask());
        Assert.Equal(0, host.Default.Streams);
        Assert.Equal(3, host.Override.Streams);
    }

    /// <summary>Multiple streaming decoders share the configured ThreadPool CPU concurrency limit.</summary>
    [Fact]
    public void ThreadPoolDecodersShareCpuConcurrencyLimit()
    {
        using Host host = new(mode: CpuExecutionMode.ThreadPool, state: new State { DecodeMilliseconds = 3 });
        using ResourceReference<Factory> reference = host.Complete(host.Manager.AcquireAsync(_key).AsTask());
        Task<ResourceStreamSession<int>>[] opening = [host.OpenAsync(), host.OpenAsync(), host.OpenAsync()];
        ResourceStreamSession<int>[] sessions = host.Complete(Task.WhenAll(opening));
        foreach (ResourceStreamSession<int> session in sessions)
        {
            host.Track(session);
        }

        int[][] values = [new int[4], new int[4], new int[4]];
        for (int iteration = 0; iteration < 12; iteration++)
        {
            var reads = new Task<int>[sessions.Length];
            for (int index = 0; index < sessions.Length; index++)
            {
                reads[index] = sessions[index].ReadAsync(values[index]).AsTask();
            }

            Assert.All(host.Complete(Task.WhenAll(reads)), count => Assert.Equal(4, count));
        }

        Assert.Equal(1, host.State.MaximumDecoders);
        Assert.All(host.State.Calls, call => Assert.NotNull(call.Context));
    }

    private sealed class Host : IDisposable
    {
        private readonly MemoryManager _memory = new();
        private readonly ExecutionRuntime _execution;
        private readonly List<ResourceStreamSession<int>> _sessions = [];
        private readonly CpuExecutionMode _mode;

        internal Host(int capacity = 8, CpuExecutionMode mode = CpuExecutionMode.Cooperative, State? state = null, bool useOverride = false)
        {
            State = state ?? new();
            _mode = mode;
            var dispatcher = new ResourceRuntime();
            Default = new(dispatcher);
            Override = new(dispatcher);
            _execution = new([dispatcher]);
            _execution.PrepareOnCurrentHost(OwnerPlacement.Host, mode, 1, new ExecutionOptions { MaxInFlightRuns = capacity, MaxCommands = capacity });
            _execution.StartAsync().GetAwaiter().GetResult();
            Manager = new("streams", new UnusedSource(), [new Stage(State)], _memory, Default, stageRuntimes: useOverride ? [new(typeof(Factory), Override)] : null);
        }

        internal State State { get; }

        internal ResourceManager Manager { get; }

        internal RecordingRuntime Default { get; }

        internal RecordingRuntime Override { get; }

        internal ulong CommittedBytes => _memory.GetSnapshot(new("cpu")).CommittedBytes;

        public void Dispose()
        {
            foreach (ResourceStreamSession<int> session in _sessions)
            {
                Complete(session.DisposeAsync().AsTask());
            }

            Complete(Manager.DisposeAsync().AsTask());
            Complete(_execution.StopAsync().AsTask());
            Complete(_memory.DisposeAsync().AsTask());
        }

        internal Task<ResourceStreamSession<int>> OpenAsync(ResourceStreamOptions? options = null, CancellationToken cancellationToken = default) => Manager.OpenStreamAsync<Factory, int>(_key, options ?? _options, cancellationToken).AsTask();

        internal ResourceStreamSession<int> Track(ResourceStreamSession<int> session)
        {
            _sessions.Add(session);
            return session;
        }

        internal void Until(Func<bool> done)
        {
            long started = Stopwatch.GetTimestamp();
            while (!done())
            {
                if (_mode == CpuExecutionMode.Cooperative)
                {
                    _execution.PumpJobs(1, TimeSpan.FromMilliseconds(2));
                }

                if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(10))
                {
                    throw new TimeoutException("Streaming did not complete while its execution runtime was running.");
                }

                Thread.Sleep(1);
            }
        }

        internal void Complete(Task task)
        {
            Until(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        internal T Complete<T>(Task<T> task)
        {
            Complete((Task)task);
            return task.GetAwaiter().GetResult();
        }
    }

    private sealed class RecordingRuntime(IResourceExecutor inner) : IResourceRuntime, IResourceExecutor
    {
        private int _streams;

        public IResourceExecutor Executor => this;

        public bool ShouldYield => inner.ShouldYield;

        internal int Streams => Volatile.Read(ref _streams);

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => inner.BuildAsync(context, cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => inner.ReleaseAsync(stage, runtime, resource);

        public ValueTask StreamAsync(IResourceStreamWork work, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _streams);
            return inner.StreamAsync(work, cancellationToken);
        }
    }

    private sealed class State
    {
        private int _activeDecoders;
        private int _maximumDecoders;

        internal int Builds => Calls.Count(static call => call.Operation == "build");

        internal int Opened => Calls.Count(static call => call.Operation == "open");

        internal int Reads => Calls.Count(static call => call.Operation == "read");

        internal int Decoded => Calls.Count(static call => call.Operation == "decoded");

        internal int Seeks => Calls.Count(static call => call.Operation == "seek");

        internal int Disposed => Calls.Count(static call => call.Operation == "dispose");

        internal int MaximumDecoders => Volatile.Read(ref _maximumDecoders);

        internal ConcurrentQueue<(string Operation, SynchronizationContext? Context, int Thread)> Calls { get; } = new();

        internal TaskCompletionSource? ReadGate { get; init; }

        internal TaskCompletionSource? SeekReadGate { get; init; }

        internal int DecodeMilliseconds { get; init; }

        internal void Record(string operation) => Calls.Enqueue((operation, SynchronizationContext.Current, Environment.CurrentManagedThreadId));

        internal void EnterDecoder()
        {
            int active = Interlocked.Increment(ref _activeDecoders);
            int previous;
            do
            {
                previous = Volatile.Read(ref _maximumDecoders);
            }
            while (active > previous && Interlocked.CompareExchange(ref _maximumDecoders, active, previous) != previous);
        }

        internal void ExitDecoder() => Interlocked.Decrement(ref _activeDecoders);
    }

    private sealed class Stage(State state) : IResourceSourceStage<Factory>
    {
        public ValueTask<Factory> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            state.Record("build");
            return new(new Factory(state));
        }

        public ValueTask ReleaseAsync(Factory resource) => ValueTask.CompletedTask;
    }

    private sealed class Factory(State state) : IResourceStream<int>
    {
        public ValueTask<IResourceStreamReader<int>> OpenReaderAsync(ResourceStreamOptions options, IMemoryScope memory, CancellationToken cancellationToken = default)
        {
            state.Record("open");
            return new(new Reader(state));
        }
    }

    private sealed class Reader(State state) : IResourceStreamReader<int>
    {
        private int _position;
        private bool _waited;
        private bool _hasSought;

        public bool CanSeek => true;

        public async ValueTask<int> ReadAsync(Memory<int> destination, CancellationToken cancellationToken = default)
        {
            state.Record("read");
            TaskCompletionSource? gate = _hasSought ? state.SeekReadGate : state.ReadGate;
            if (!_waited && gate is not null)
            {
                _waited = true;
                await gate.Task.WaitAsync(cancellationToken);
                state.Record("continue");
            }

            cancellationToken.ThrowIfCancellationRequested();
            state.EnterDecoder();

            try
            {
                if (state.DecodeMilliseconds > 0)
                {
                    Thread.Sleep(state.DecodeMilliseconds);
                }

                for (int index = 0; index < destination.Length; index++)
                {
                    destination.Span[index] = _position++;
                }

                state.Record("decoded");
                return destination.Length;
            }
            finally
            {
                state.ExitDecoder();
            }
        }

        public ValueTask SeekAsync(ulong elementOffset, CancellationToken cancellationToken = default)
        {
            state.Record("seek");
            _position = checked((int)elementOffset);
            _hasSought = true;
            _waited = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            state.Record("dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnusedSource : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
    }
}
