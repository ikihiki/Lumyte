using System.Runtime.ExceptionServices;
using Lumyte.Memory;

namespace Lumyte.Resources;

/// <summary>Owns an independent reader and a bounded single-producer, single-consumer buffer.</summary>
/// <remarks>
/// Reads and seeks require one consumer and must not overlap each other or disposal.
/// Consuming a block schedules its waiting producer through the factory executor.
/// Reader operations run in jobs; ReadAvailable never invokes the reader inline.
/// Cooperative execution requires pumping jobs until session shutdown completes.
/// </remarks>
/// <typeparam name="TElement">The unmanaged stream element.</typeparam>
public sealed class ResourceStreamSession<TElement> : IAsyncDisposable, IResourceStreamWork
    where TElement : unmanaged
{
    private readonly IResourceExecutor _executor;
    private readonly IMemoryScope _memory;
    private readonly IDisposable _lease;
    private readonly ResourceStreamOptions _options;
    private readonly Action _onDisposed;
    private readonly Block[] _blocks;
    private readonly ResourceStreamProducerSignal _spaceAvailable = new();
    private readonly ResourceStreamSignal _dataAvailable = new();
    private IResourceStream<TElement>? _factory;
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
    private bool _disposeReader;
    private ulong? _seekOffset;

    internal ResourceStreamSession(
        IResourceExecutor executor,
        IMemoryScope memory,
        IDisposable lease,
        ulong revision,
        ResourceStreamOptions options,
        Action onDisposed)
    {
        _executor = executor;
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

            ValueTask ready = _dataAvailable.WaitAsync(cancellationToken);

            // Register before checking again so publication cannot lose a wakeup.
            if (_blocks[_readIndex].Count != 0 || EndOfStream || Volatile.Read(ref _failure) is not null)
            {
                _dataAvailable.Signal();
            }

            await ready.ConfigureAwait(false);
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
            _seekOffset = elementOffset;
            _firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (StartProducer(cancellationToken))
            {
                await _firstReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
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

    /// <inheritdoc/>
    async ValueTask IResourceStreamWork.ExecuteAsync(CancellationToken cancellationToken)
    {
        if (_disposeReader)
        {
            await _reader!.DisposeAsync();
            _reader = null;
            return;
        }

        if (_reader is null)
        {
            _reader = await _factory!.OpenReaderAsync(_options, _memory, cancellationToken);
            _factory = null;
            _initialized = true;
        }
        else if (_seekOffset is { } offset)
        {
            await _reader.SeekAsync(offset, cancellationToken);
            foreach (Block buffered in _blocks)
            {
                buffered.Count = 0;
            }

            _readIndex = 0;
            _writeIndex = 0;
            _readOffset = 0;
            Position = offset;
            _finished = false;
            _seekOffset = null;
        }

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

                await available;
                continue;
            }

            if (_executor.ShouldYield)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }

            int count = await _reader.ReadAsync(block.Buffer!.Memory[.._options.BlockSize], cancellationToken);
            if ((uint)count > (uint)_options.BlockSize)
            {
                throw new InvalidOperationException("A resource stream reader returned an invalid element count.");
            }

            if (count == 0)
            {
                Volatile.Write(ref _finished, true);
                _firstReady.TrySetResult();
                _dataAvailable.Signal();
                return;
            }

            block.Count = count;
            _writeIndex = (_writeIndex + 1) % _blocks.Length;
            _firstReady.TrySetResult();
            _dataAvailable.Signal();
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

        cancellationToken.ThrowIfCancellationRequested();
        _factory = factory;
        using (StartProducer(cancellationToken))
        {
            await _firstReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
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

    private CancellationTokenRegistration StartProducer(CancellationToken cancellationToken)
    {
        _producerCancellation = new CancellationTokenSource();

        // Cancel queued jobs immediately while opening/seeking, then detach before returning the session.
        CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(
            static source => ((CancellationTokenSource)source!).Cancel(), _producerCancellation);
        _producer = ProduceAsync(_producerCancellation.Token);
        return registration;
    }

    private async Task ProduceAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Observe admission failures as well as reader failures, including before the job starts.
            await _executor.StreamAsync(this, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _failure, ExceptionDispatchInfo.Capture(exception));
            _firstReady.TrySetResult();
            _dataAvailable.Signal();
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
                _disposeReader = true;
                await _executor.StreamAsync(this, CancellationToken.None).ConfigureAwait(false);
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
