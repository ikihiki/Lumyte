using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Lumyte.Resources;

/// <summary>Watches source hints and rebuilds resources in bounded, debounced batches.</summary>
/// <remarks>
/// ResourceManager owns its configured worker and stops it before releasing resources.
/// Explicitly constructed workers borrow both manager and change source and must stop before either owner is disposed.
/// Events execute synchronously on the background worker, outside synchronization locks. Handlers must
/// not synchronously wait for shutdown; a handler exception faults <see cref="Completion"/>.
/// Source enumeration failures also fault completion. Reload failures retain the old graph and allow later batches.
/// </remarks>
public sealed class ResourceReloadService : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ResourceManager _manager;
    private readonly IResourceChangeSource _source;
    private readonly ResourceReloadOptions _options;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _changed = new(0, 1);
    private readonly HashSet<ResourceId> _pending = [];
    private Task _completion = Task.CompletedTask;
    private Task? _stopTask;
    private Task? _disposeTask;
    private ResourceReloadResult? _lastResult;
    private Exception? _lastError;
    private long _firstChange;
    private long _lastChange;
    private bool _rescan;
    private bool _watchFinished;
    private bool _started;
    private bool _stopRequested;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ResourceReloadService"/> class.</summary>
    /// <param name="manager">The resource graph to reload.</param>
    /// <param name="source">The change notification source.</param>
    /// <param name="options">Batching and retry limits, or null for defaults.</param>
    public ResourceReloadService(ResourceManager manager, IResourceChangeSource source, ResourceReloadOptions? options = null)
    {
        _manager = manager;
        _source = source;
        _options = options ?? new();
        _options.Validate();
    }

    /// <summary>Occurs after each completed attempt, including conflicts that will be retried.</summary>
    public event EventHandler<ResourceReloadEventArgs>? Reloaded;

    /// <summary>Occurs when a reload throws; the previous published graph remains available.</summary>
    public event EventHandler<ResourceReloadFailureEventArgs>? ReloadFailed;

    /// <summary>Gets the worker lifetime task, or an already completed task before starting.</summary>
    public Task Completion
    {
        get
        {
            lock (_gate)
            {
                return _completion;
            }
        }
    }

    /// <summary>Gets the latest completed reload result, or null before the first completed attempt.</summary>
    public ResourceReloadResult? LastResult => Volatile.Read(ref _lastResult);

    /// <summary>Gets the latest reload or terminal worker exception, cleared by a completed reload attempt.</summary>
    public Exception? LastError => Volatile.Read(ref _lastError);

    /// <summary>Starts background watching once; repeated calls while active have no effect.</summary>
    /// <remarks>A stopped service cannot restart. Resolve a new service scope for a new lifetime.</remarks>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopRequested)
            {
                throw new InvalidOperationException("A stopped reload service cannot restart.");
            }

            if (!_started)
            {
                _started = true;
                _completion = Task.Run(RunAsync);
            }
        }
    }

    /// <summary>Cancels watching and in-flight reloads, then joins both workers.</summary>
    /// <returns>Completion of shutdown; terminal worker errors remain observable.</returns>
    public Task StopAsync()
    {
        lock (_gate)
        {
            _stopRequested = true;
            return _stopTask ??= Task.Run(StopCoreAsync);
        }
    }

    /// <summary>Stops the worker and releases coordinator state without disposing borrowed services.</summary>
    /// <returns>Completion of disposal, shared by concurrent callers; worker errors remain on <see cref="Completion"/> and <see cref="LastError"/>.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
            _stopRequested = true;
            return new(_disposeTask ??= Task.Run(DisposeCoreAsync));
        }
    }

    internal void RequestRescan()
    {
        lock (_gate)
        {
            if (_stopRequested || _disposed)
            {
                return;
            }

            _firstChange = _lastChange = Stopwatch.GetTimestamp();
            _rescan = true;
            _pending.Clear();
            SignalChange();
        }
    }

    private async Task RunAsync()
    {
        CancellationToken token = _cancellation.Token;
        var watcher = Task.Run(() => WatchAsync(token));
        Task consumer = ConsumeAsync(token);
        Exception? failure = null;
        try
        {
            Task first = await Task.WhenAny(watcher, consumer).ConfigureAwait(false);
            await first.ConfigureAwait(false);
            await Task.WhenAll(watcher, consumer).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                await _cancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (failure is null || failure is OperationCanceledException)
                {
                    failure = exception;
                }
            }

            try
            {
                await Task.WhenAll(watcher, consumer).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (failure is null || (failure is OperationCanceledException && exception is not OperationCanceledException))
                {
                    failure = exception;
                }
            }
        }

        bool stopping;
        lock (_gate)
        {
            stopping = _stopRequested;
        }

        if (failure is not null && !(failure is OperationCanceledException && stopping))
        {
            Volatile.Write(ref _lastError, failure);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private async Task WatchAsync(CancellationToken token)
    {
        try
        {
            await foreach (ResourceSourceChange change in _source.WatchAsync(_manager.WatchTargets, token).WithCancellation(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (!_rescan && _pending.Count == 0)
                    {
                        _firstChange = now;
                    }

                    _lastChange = now;
                    if (change.RequiresRescan || (!_rescan && !_pending.Contains(change.Id) && _pending.Count >= _options.MaximumPendingChanges))
                    {
                        _rescan = true;
                        _pending.Clear();
                    }
                    else if (!_rescan)
                    {
                        _pending.Add(change.Id);
                    }

                    SignalChange();
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _watchFinished = true;
                SignalChange();
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            ResourceId[]? batch = null;
            bool rescan = false;
            TimeSpan delay = Timeout.InfiniteTimeSpan;
            lock (_gate)
            {
                if (_rescan || _pending.Count != 0)
                {
                    TimeSpan quiet = _options.DebounceDelay - Stopwatch.GetElapsedTime(_lastChange);
                    TimeSpan maximum = _options.MaximumBatchDelay - Stopwatch.GetElapsedTime(_firstChange);
                    delay = quiet < maximum ? quiet : maximum;
                    if (delay <= TimeSpan.Zero)
                    {
                        batch = _pending.ToArray();
                        rescan = _rescan;
                        _pending.Clear();
                        _rescan = false;
                    }
                }
                else if (_watchFinished)
                {
                    return;
                }
            }

            if (batch is not null)
            {
                await ReloadBatchAsync(batch, rescan, token).ConfigureAwait(false);
            }
            else
            {
                await _changed.WaitAsync(delay, token).ConfigureAwait(false);
            }
        }
    }

    private async Task ReloadBatchAsync(ResourceId[] batch, bool rescan, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            ResourceReloadResult? result = null;
            Exception? failure = null;
            try
            {
                result = await _manager.ReloadFromChangesAsync(batch, rescan, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            bool retry = attempt < _options.MaximumRetryCount;
            if (failure is not null)
            {
                Volatile.Write(ref _lastError, failure);
                ReloadFailed?.Invoke(this, new(failure, attempt + 1, retry));
            }
            else
            {
                Volatile.Write(ref _lastResult, result);
                Volatile.Write(ref _lastError, null);
                Reloaded?.Invoke(this, new(result!));
                retry &= result!.Status == ResourceReloadStatus.Conflict;
            }

            if (!retry)
            {
                return;
            }

            await Task.Delay(_options.RetryDelay, token).ConfigureAwait(false);
        }
    }

    private void SignalChange()
    {
        // All producers hold _gate; the consumer can only decrease this count.
        if (_changed.CurrentCount == 0)
        {
            _changed.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        Exception? failure = null;
        try
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        if (failure is not null)
        {
            Volatile.Write(ref _lastError, failure);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Joining has completed. Retain the diagnostic without preventing DI from disposing borrowed owners.
        }
        finally
        {
            _cancellation.Dispose();
            _changed.Dispose();
        }
    }
}
