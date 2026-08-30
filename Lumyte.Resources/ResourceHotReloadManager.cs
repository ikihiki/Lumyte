namespace Lumyte.Resources;

public sealed class ResourceHotReloadManager : IAsyncDisposable
{
    private readonly ResourceStore store;
    private readonly IAssetChangeSource[] sources;
    private readonly ResourceHotReloadOptions options;
    private readonly Dictionary<AssetChange, ReloadWork> pending = [];
    private readonly Lock gate = new();
    private readonly CancellationTokenSource shutdown = new();
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
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
        {
            throw new InvalidOperationException("Resource hot reload can only be started once.");
        }

        foreach (IAssetChangeSource source in sources)
        {
            source.Changed += OnChanged;
        }
    }

    public async ValueTask DisposeAsync()
    {
        int previous = Interlocked.Exchange(ref state, 2);
        if (previous == 2)
        {
            return;
        }

        foreach (IAssetChangeSource source in sources)
        {
            source.Changed -= OnChanged;
        }

        shutdown.Cancel();
        Task[] tasks;
        lock (gate)
        {
            foreach (ReloadWork work in pending.Values)
            {
                work.Cancellation.Cancel();
            }

            tasks = pending.Values.Select(work => work.Task).ToArray();
            pending.Clear();
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        shutdown.Dispose();
    }

    private void OnChanged(AssetChange change)
    {
        if (Volatile.Read(ref state) != 1)
        {
            return;
        }

        lock (gate)
        {
            if (pending.Remove(change, out ReloadWork? previous))
            {
                previous.Cancellation.Cancel();
            }

            CancellationTokenSource cancellation =
                CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            ReloadWork work = new(cancellation);
            pending.Add(change, work);
            work.Task = ProcessAsync(change, work);
        }
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
            ReloadFailed?.Invoke(new ResourceHotReloadFailure(change, exception));
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
            }

            work.Cancellation.Dispose();
        }
    }

    private sealed class ReloadWork(CancellationTokenSource cancellation)
    {
        internal CancellationTokenSource Cancellation { get; } = cancellation;

        internal Task Task { get; set; } = Task.CompletedTask;
    }
}
