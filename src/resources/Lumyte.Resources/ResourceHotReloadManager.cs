namespace Lumyte.Resources;

public sealed class ResourceHotReloadManager : IAsyncDisposable
{
    private readonly ResourceStore store;
    private readonly IAssetChangeSource[] sources;
    private readonly ResourceHotReloadOptions options;
    private readonly Dictionary<AssetChange, ReloadWork> pending = [];
    private readonly HashSet<ReloadWork> active = [];
    private readonly Lock gate = new();
    private Task? disposal;
    private Exception? notificationFailure;
    private int state;

    public ResourceHotReloadManager(
        ResourceStore store,
        IEnumerable<IAssetChangeSource> sources,
        ResourceHotReloadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sources);
        this.store = store;
        this.sources = sources.ToArray();
        if (this.sources.Any(source => source is null))
        {
            throw new ArgumentException("Asset change sources cannot contain null.", nameof(sources));
        }

        this.options = options ?? new ResourceHotReloadOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.DebounceDelay, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(this.options.TimeProvider);
    }

    public event Action<ResourceHotReloadResult>? Reloaded;

    public event Action<ResourceHotReloadFailure>? ReloadFailed;

    public void Start()
    {
        lock (gate)
        {
            if (state != 0)
            {
                throw new InvalidOperationException("Resource hot reload can only be started once.");
            }
            state = 1;
            foreach (IAssetChangeSource source in sources)
            {
                source.Changed += OnChanged;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is not null) { return new(disposal); }
            state = 2;
            ReloadWork[] works = active.ToArray();
            disposal = DrainAsync(works);
            foreach (IAssetChangeSource source in sources)
            {
                source.Changed -= OnChanged;
            }
            foreach (ReloadWork work in works)
            {
                if (active.Contains(work)) { CancelWork(work); }
            }
            pending.Clear();
            return new(disposal);
        }
    }

    private async Task DrainAsync(ReloadWork[] works)
    {
        await Task.WhenAll(works.Select(work => work.Completion.Task)).ConfigureAwait(false);
        lock (gate)
        {
            if (notificationFailure is { } failure) { throw new AggregateException("Hot reload notification failed.", failure); }
        }
    }

    private void OnChanged(AssetChange change)
    {
        ReloadWork work;
        lock (gate)
        {
            if (state != 1) { return; }
            if (pending.Remove(change, out ReloadWork? previous))
            {
                CancelWork(previous);
                if (state != 1) { return; }
            }

            CancellationTokenSource cancellation = new();
            work = new(cancellation);
            pending.Add(change, work);
            active.Add(work);
        }
        _ = ProcessAsync(change, work);
    }

    private void CancelWork(ReloadWork work)
    {
        try { work.Cancellation.Cancel(); }
        catch (Exception error) { notificationFailure ??= error; }
    }

    private async Task ProcessAsync(AssetChange change, ReloadWork work)
    {
        try
        {
            await Task.Delay(
                    options.DebounceDelay,
                    options.TimeProvider,
                    work.Cancellation.Token)
                .ConfigureAwait(false);
            int count = await store.ReloadChangedAssetAsync(
                    change,
                    work.Cancellation.Token)
                .ConfigureAwait(false);
            ResourcesDiagnostics.HotReloadOperations.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "succeeded"));
            Reloaded?.Invoke(new ResourceHotReloadResult(change, count));
        }
        catch (OperationCanceledException) when (work.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ResourcesDiagnostics.HotReloadOperations.Add(
                1,
                new("outcome", "failed"),
                new("error.type", exception.GetType().Name));
            try
            {
                ReloadFailed?.Invoke(new ResourceHotReloadFailure(change, exception));
            }
            catch (Exception notificationException)
            {
                lock (gate) { notificationFailure ??= notificationException; }
            }
        }
        finally
        {
            lock (gate)
            {
                if (pending.TryGetValue(change, out ReloadWork? current)
                    && ReferenceEquals(current, work))
                {
                    pending.Remove(change);
                }
                active.Remove(work);
                work.Cancellation.Dispose();
                work.Completion.TrySetResult();
            }
        }
    }

    private sealed class ReloadWork(CancellationTokenSource cancellation)
    {
        internal CancellationTokenSource Cancellation { get; } = cancellation;

        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
