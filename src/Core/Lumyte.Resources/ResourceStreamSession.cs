using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Sources;
using Lumyte.Memory;

namespace Lumyte.Resources;

/// <summary>Owns an independent reader and a bounded single-producer, single-consumer buffer.</summary>
/// <remarks>
/// Reads and seeks require one consumer and must not overlap each other or disposal.
/// Consuming a block wakes a waiting producer through a reusable queued notification.
/// Reader work and producer continuations never run inline on the consumer's thread.
/// </remarks>
/// <typeparam name="TElement">The unmanaged stream element.</typeparam>
public sealed class ResourceStreamSession<TElement> : IAsyncDisposable
    where TElement : unmanaged
{
    private readonly IMemoryScope _memory;
    private readonly IDisposable _lease;
    private readonly ResourceStreamOptions _options;
    private readonly Action _onDisposed;
    private readonly Block[] _blocks;
    private readonly ProducerSignal _spaceAvailable = new();
    private IResourceStreamReader<TElement>? _reader;
    private CancellationTokenSource? _producerCancellation;
    private Task _producer = Task.CompletedTask;
    private TaskCompletionSource _firstReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _disposal;
    private ExceptionDispatchInfo? _failure;
    private int _readIndex;
    private int _writeIndex;
    private int _readOffset;
    private bool _finished;
    private bool _initialized;
    private bool _memoryDisposed;
    private bool _leaseDisposed;

    internal ResourceStreamSession(
        IMemoryScope memory,
        IDisposable lease,
        ulong revision,
        ResourceStreamOptions options,
        Action onDisposed)
    {
        _memory = memory;
        _lease = lease;
        Revision = revision;
        _options = options;
        _onDisposed = onDisposed;
        _blocks = new Block[options.MaxBufferedBlocks];
        for (int index = 0; index < _blocks.Length; index++)
        {
            _blocks[index] = new Block();
        }
    }

    /// <summary>Gets the immutable factory revision retained by this session.</summary>
    public ulong Revision { get; }

    /// <summary>Gets the number of elements consumed, or the last seek offset plus subsequent consumption.</summary>
    public ulong Position { get; private set; }

    /// <summary>Gets a value indicating whether this session's reader can seek.</summary>
    public bool CanSeek => _reader?.CanSeek == true;

    /// <summary>Gets a value indicating whether the producer reached the end and all buffered elements have been consumed.</summary>
    public bool EndOfStream => Volatile.Read(ref _finished) && _blocks[_readIndex].Count == 0;

    /// <summary>Reads available elements, asynchronously waiting for data when necessary.</summary>
    /// <param name="destination">Storage for decoded elements.</param>
    /// <param name="cancellationToken">Cancels only this read's wait, without stopping the producer.</param>
    /// <returns>The number consumed; zero means end of stream or an empty destination.</returns>
    public async ValueTask<int> ReadAsync(Memory<TElement> destination, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = ReadAvailable(destination.Span);
            if (count != 0 || destination.IsEmpty || EndOfStream)
            {
                return count;
            }

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Copies buffered elements without I/O or waiting, reusing producer notifications.</summary>
    /// <param name="destination">Storage for decoded elements.</param>
    /// <returns>The number consumed; zero can mean underflow or end of stream.</returns>
    public int ReadAvailable(Span<TElement> destination)
    {
        ThrowIfUnavailable();
        int total = 0;
        while (total < destination.Length)
        {
            Block block = _blocks[_readIndex];
            int available = block.Count;
            if (available == 0)
            {
                break;
            }

            int count = Math.Min(available - _readOffset, destination.Length - total);
            ulong position = checked(Position + (ulong)count);
            block.Buffer!.Memory.Span.Slice(_readOffset, count).CopyTo(destination[total..]);
            _readOffset += count;
            total += count;
            Position = position;
            if (_readOffset == available)
            {
                _readOffset = 0;
                _readIndex = (_readIndex + 1) % _blocks.Length;
                block.Count = 0;
                _spaceAvailable.Signal();
            }
        }

        return total;
    }

    /// <summary>Stops prefetching, discards buffered elements and starts reading from another position.</summary>
    /// <param name="elementOffset">The absolute element offset.</param>
    /// <param name="cancellationToken">Cancels seeking. A cancelled or failed seek faults the session.</param>
    /// <returns>Completion after the first block or end of stream is available at the new position.</returns>
    public async ValueTask SeekAsync(ulong elementOffset, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        if (!CanSeek)
        {
            throw new NotSupportedException("This resource stream does not support seeking.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await StopProducerAsync().ConfigureAwait(false);
            ThrowIfUnavailable();
            await _reader!.SeekAsync(elementOffset, cancellationToken).ConfigureAwait(false);
            foreach (Block block in _blocks)
            {
                block.Count = 0;
            }

            _readIndex = 0;
            _writeIndex = 0;
            _readOffset = 0;
            Position = elementOffset;
            _finished = false;
            _firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            StartProducer();
            await _firstReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            ThrowIfUnavailable();
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _failure, ExceptionDispatchInfo.Capture(exception));
            await StopProducerAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Stops the producer, then releases the reader, buffers, memory scope and factory lease.</summary>
    /// <remarks>Concurrent calls share one attempt. A failed cleanup can be retried by calling again.</remarks>
    /// <returns>Completion of the current cleanup attempt.</returns>
    public ValueTask DisposeAsync()
    {
        while (true)
        {
            TaskCompletionSource? previous = Volatile.Read(ref _disposal);
            if (previous is not null && !previous.Task.IsFaulted)
            {
                return new ValueTask(previous.Task);
            }

            var attempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _disposal, attempt, previous), previous))
            {
                _ = CompleteDisposalAsync(attempt);
                return new ValueTask(attempt.Task);
            }
        }
    }

    internal async ValueTask InitializeAsync(IResourceStream<TElement> factory, CancellationToken cancellationToken)
    {
        foreach (Block block in _blocks)
        {
            block.Buffer = await _memory.RentAsync<TElement>(
                _options.BufferPool,
                _options.BlockSize,
                MemoryPurpose.Streaming,
                cancellationToken).ConfigureAwait(false);
        }

        _reader = await factory.OpenReaderAsync(_options, _memory, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _initialized = true;
        StartProducer();
        await _firstReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfUnavailable();
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposal) is not null, this);
        Volatile.Read(ref _failure)?.Throw();
        if (!_initialized)
        {
            throw new InvalidOperationException("The resource stream has not been initialized.");
        }
    }

    private void StartProducer()
    {
        _producerCancellation = new CancellationTokenSource();
        _producer = ProduceAsync(_producerCancellation.Token);
    }

    private async Task ProduceAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Block block = _blocks[_writeIndex];
                if (block.Count != 0)
                {
                    ValueTask available = _spaceAvailable.WaitAsync();

                    // Register before checking again so consuming the block cannot lose a wakeup.
                    if (block.Count == 0 || cancellationToken.IsCancellationRequested)
                    {
                        _spaceAvailable.Signal();
                    }

                    await available.ConfigureAwait(false);
                    continue;
                }

                int count = await _reader!.ReadAsync(block.Buffer!.Memory[.._options.BlockSize], cancellationToken).ConfigureAwait(false);
                if ((uint)count > (uint)_options.BlockSize)
                {
                    throw new InvalidOperationException("A resource stream reader returned an invalid element count.");
                }

                if (count == 0)
                {
                    Volatile.Write(ref _finished, true);
                    _firstReady.TrySetResult();
                    return;
                }

                block.Count = count;
                _writeIndex = (_writeIndex + 1) % _blocks.Length;
                _firstReady.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _failure, ExceptionDispatchInfo.Capture(exception));
            _firstReady.TrySetResult();
        }
    }

    private async ValueTask StopProducerAsync()
    {
        if (_producerCancellation is not null)
        {
            if (!_producerCancellation.IsCancellationRequested)
            {
                await _producerCancellation.CancelAsync().ConfigureAwait(false);
            }

            _spaceAvailable.Signal();
            await _producer.ConfigureAwait(false);
            _producerCancellation.Dispose();
            _producerCancellation = null;
        }
    }

    private async Task CompleteDisposalAsync(TaskCompletionSource attempt)
    {
        try
        {
            await StopProducerAsync().ConfigureAwait(false);
            if (_reader is not null)
            {
                await _reader.DisposeAsync().ConfigureAwait(false);
                _reader = null;
            }

            foreach (Block block in _blocks)
            {
                block.Buffer?.Dispose();
                block.Buffer = null;
            }

            if (!_memoryDisposed)
            {
                await _memory.DisposeAsync().ConfigureAwait(false);
                _memoryDisposed = true;
            }

            if (!_leaseDisposed)
            {
                _lease.Dispose();
                _leaseDisposed = true;
            }

            _onDisposed();
            attempt.TrySetResult();
        }
        catch (Exception exception)
        {
            attempt.TrySetException(exception);
        }
    }

    // There is one producer wait at a time. Completion runs on the reusable work item,
    // so releasing a block neither allocates a Task nor invokes the reader inline.
    private sealed class ProducerSignal : IValueTaskSource, IThreadPoolWorkItem
    {
        private ManualResetValueTaskSourceCore<bool> _completion;
        private int _waiting;

        public void Execute() => _completion.SetResult(true);

        public void GetResult(short token) => _completion.GetResult(token);

        public ValueTaskSourceStatus GetStatus(short token) => _completion.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _completion.OnCompleted(continuation, state, token, flags);

        internal ValueTask WaitAsync()
        {
            _completion.Reset();
            Volatile.Write(ref _waiting, 1);
            return new ValueTask(this, _completion.Version);
        }

        internal void Signal()
        {
            if (Interlocked.Exchange(ref _waiting, 0) != 0)
            {
                ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
            }
        }
    }

    private sealed class Block
    {
        private int _count;

        internal IMemoryBuffer<TElement>? Buffer { get; set; }

        internal int Count
        {
            get => Volatile.Read(ref _count);
            set => Volatile.Write(ref _count, value);
        }
    }
}
