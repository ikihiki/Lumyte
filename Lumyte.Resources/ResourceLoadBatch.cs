namespace Lumyte.Resources;

public sealed class ResourceLoadBatch
{
    private readonly ResourceStore store;
    private readonly ResourceLoadBatchOptions options;
    private readonly List<IBatchItem> items = [];
    private int state;

    internal ResourceLoadBatch(ResourceStore store, ResourceLoadBatchOptions options)
    {
        this.store = store;
        this.options = options;
    }

    public event Action<ResourceLoadProgress>? ProgressChanged;

    public int Count => items.Count;

    public ResourceLoadBatchItem<T> Add<T>(AssetKey<T> key)
        where T : notnull
    {
        if (Volatile.Read(ref state) != 0)
        {
            throw new InvalidOperationException(
                "Items cannot be added after the batch has started.");
        }

        ResourceLoadBatchItem<T> item = new(this, key, items.Count);
        items.Add(new BatchItem<T>(item));
        return item;
    }

    public ValueTask<ResourceLoadBatchResult> LoadAsync(
        CancellationToken cancellationToken = default) =>
        LoadAsync(scope: null, cancellationToken);

    internal async ValueTask<ResourceLoadBatchResult> LoadAsync(
        ResourceScope? scope,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
        {
            throw new InvalidOperationException("A resource load batch can only run once.");
        }

        ResourceLoadOptions loadOptions = new() { Priority = options.Priority };
        IBatchItem[] snapshot = [.. items];
        var failures = new ResourceLoadBatchFailure?[snapshot.Length];
        int completed = 0;
        int succeeded = 0;
        int failed = 0;
        ReportProgress();

        Task[] loads = snapshot.Select(LoadItemAsync).ToArray();
        await Task.WhenAll(loads).ConfigureAwait(false);
        Volatile.Write(ref state, 2);
        return new ResourceLoadBatchResult(
            this,
            snapshot.Length,
            succeeded,
            failures.Where(failure => failure is not null).Cast<ResourceLoadBatchFailure>().ToArray());

        async Task LoadItemAsync(IBatchItem item)
        {
            try
            {
                await item.LoadAsync(store, scope, loadOptions, cancellationToken)
                    .ConfigureAwait(false);
                Interlocked.Increment(ref succeeded);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures[item.Index] = new ResourceLoadBatchFailure(
                    item.Key,
                    item.ResourceType,
                    exception);
                Interlocked.Increment(ref failed);
            }
            finally
            {
                Interlocked.Increment(ref completed);
                ReportProgress();
            }
        }

        void ReportProgress() => ProgressChanged?.Invoke(
            new ResourceLoadProgress(
                Volatile.Read(ref completed),
                snapshot.Length,
                Volatile.Read(ref succeeded),
                Volatile.Read(ref failed)));
    }

    internal ResourceStore Store => store;

    private interface IBatchItem
    {
        int Index { get; }

        string Key { get; }

        Type ResourceType { get; }

        ValueTask LoadAsync(
            ResourceStore store,
            ResourceScope? scope,
            ResourceLoadOptions options,
            CancellationToken cancellationToken);
    }

    private sealed class BatchItem<T>(ResourceLoadBatchItem<T> item) : IBatchItem
        where T : notnull
    {
        public int Index => item.Index;

        public string Key => item.Key.ToString();

        public Type ResourceType => typeof(T);

        public async ValueTask LoadAsync(
            ResourceStore store,
            ResourceScope? scope,
            ResourceLoadOptions options,
            CancellationToken cancellationToken)
        {
            item.Handle = scope is null
                ? await store.LoadAsync(item.Key, options, cancellationToken)
                    .ConfigureAwait(false)
                : await scope.LoadAsync(item.Key, options, cancellationToken)
                    .ConfigureAwait(false);
            item.Succeeded = true;
        }
    }
}
