using System.Collections.Concurrent;
using System.Diagnostics;

namespace Lumyte.Resources;

/// <summary>Coordinates resource loading, caching, dependencies, and lifetime.</summary>
public sealed class ResourceStore : IAsyncDisposable
{
    private readonly IReadOnlyDictionary<string, ResourceResolverRegistration> resolvers;
    private readonly IReadOnlyDictionary<Type, ResourceLoaderRegistration> loaders;
    private readonly ResourceStoreOptions options;
    private readonly IResourceDispatcher dispatcher;
    private readonly ResourceLoadScheduler scheduler;
    private readonly ResourceKeyTable keys = new();
    private readonly ConcurrentDictionary<uint, Lazy<Task<IResourceRecord>>> resources = new();
    private readonly ConcurrentDictionary<uint, SemaphoreSlim> reloadLocks = new();
    private readonly ConcurrentDictionary<uint, int> strongReferences = new();
    private readonly ConcurrentDictionary<uint, ResourceLoadInterest> loadInterests = new();
    private readonly ConcurrentDictionary<ResourceMemoryPool, long> memoryUsage = new();
    private readonly Lock generationLock = new();
    private long accessSequence;
    private int disposed;

    public ResourceStore(
        IEnumerable<IAssetResolver> resolvers,
        IEnumerable<IResourceLoader> loaders,
        ResourceStoreOptions? options = null,
        IResourceDispatcher? dispatcher = null)
        : this(
            CreateResolverRegistrations(resolvers),
            CreateLoaderRegistrations(loaders),
            options ?? new ResourceStoreOptions(),
            dispatcher ?? new InlineResourceDispatcher())
    {
    }

    public ResourceStore(ResourceStoreConfiguration configuration)
        : this(
            (configuration ?? throw new ArgumentNullException(nameof(configuration))).Resolvers,
            configuration.Loaders,
            configuration.Options,
            configuration.Dispatcher)
    {
    }

    private ResourceStore(
        IEnumerable<ResourceResolverRegistration> resolvers,
        IEnumerable<ResourceLoaderRegistration> loaders,
        ResourceStoreOptions options,
        IResourceDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        ArgumentNullException.ThrowIfNull(loaders);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dispatcher);

        Dictionary<string, ResourceResolverRegistration> registeredResolvers =
            new(StringComparer.Ordinal);
        foreach (ResourceResolverRegistration registration in resolvers)
        {
            ArgumentNullException.ThrowIfNull(registration);
            IAssetResolver resolver = registration.Resolver;
            string scheme = AssetKey.NormalizeScheme(resolver.Scheme);
            if (!registeredResolvers.TryAdd(scheme, registration))
            {
                throw new ArgumentException(
                    $"An asset resolver is already registered for the '{scheme}' scheme.",
                    nameof(resolvers));
            }
        }

        Dictionary<Type, ResourceLoaderRegistration> registeredLoaders = [];
        foreach (ResourceLoaderRegistration registration in loaders)
        {
            ArgumentNullException.ThrowIfNull(registration);
            IResourceLoader loader = registration.Loader;
            Type resourceType = loader.ResourceType;
            if (!registeredLoaders.TryAdd(resourceType, registration))
            {
                throw new ArgumentException(
                    $"A resource loader is already registered for '{resourceType}'.",
                    nameof(loaders));
            }
        }

        this.resolvers = registeredResolvers;
        this.loaders = registeredLoaders;
        this.options = options;
        this.dispatcher = dispatcher;
        scheduler = new ResourceLoadScheduler(this.options.Scheduling);
        foreach ((ResourceMemoryPool pool, long budget) in this.options.MemoryBudgets)
        {
            if (string.IsNullOrWhiteSpace(pool.Name) || budget < 0)
            {
                throw new ArgumentException(
                    "Resource memory budgets require a named pool and a non-negative size.",
                    nameof(options));
            }
        }
    }

    private static IEnumerable<ResourceResolverRegistration> CreateResolverRegistrations(
        IEnumerable<IAssetResolver> resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        return resolvers.Select(resolver => new ResourceResolverRegistration(resolver));
    }

    private static IEnumerable<ResourceLoaderRegistration> CreateLoaderRegistrations(
        IEnumerable<IResourceLoader> loaders)
    {
        ArgumentNullException.ThrowIfNull(loaders);
        return loaders.Select(loader => new ResourceLoaderRegistration(loader));
    }

    internal int InternedKeyCount => keys.Count;

    public ResourceScope CreateScope(ResourceScopeOptions? options = null) =>
        new(this, options ?? new ResourceScopeOptions());

    public ResourceLoadBatch CreateLoadBatch(ResourceLoadBatchOptions? options = null) =>
        new(this, options ?? new ResourceLoadBatchOptions());

    public async ValueTask<ResourcePin<T>> PinAsync<T>(
        AssetKey<T> key,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ResourceHandle<T> handle = await LoadAsync(key, cancellationToken)
            .ConfigureAwait(false);
        return new ResourcePin<T>(this, handle);
    }

    public ValueTask<ResourceHandle<T>> LoadAsync<T>(
        AssetKey<T> key,
        CancellationToken cancellationToken = default)
        where T : notnull =>
        LoadAsync(key, new ResourceLoadOptions(), cancellationToken);

    public ValueTask<ResourceHandle<T>> LoadAsync<T>(
        AssetKey<T> key,
        ResourceLoadOptions options,
        CancellationToken cancellationToken = default)
        where T : notnull =>
        LoadAsync(key, path: null, options, cancellationToken);

    /// <summary>Captures the currently loaded generations as one stable view.</summary>
    public ResourceSnapshot CreateSnapshot()
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);

        lock (generationLock)
        {
            Dictionary<uint, IResourceRecord> snapshot = [];
            foreach ((uint slot, Lazy<Task<IResourceRecord>> pending) in resources)
            {
                if (pending.IsValueCreated && pending.Value.IsCompletedSuccessfully)
                {
                    snapshot.Add(slot, pending.Value.Result);
                }
            }

            return new ResourceSnapshot(this, snapshot);
        }
    }

    /// <summary>Loads a new generation and atomically makes it current.</summary>
    public async ValueTask<ResourceHandle<T>> ReloadAsync<T>(
        AssetKey<T> key,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);

        using Activity? activity = ResourcesDiagnostics.Activities.StartActivity(
            "ResourceStore.Reload",
            ActivityKind.Internal);
        activity?.SetTag("resource.type", typeof(T).FullName);
        activity?.SetTag("asset.scheme", key.Scheme.ToString());

        try
        {
            ResourceKeyEntry entry = keys.GetOrAdd(key);
            if (!resources.ContainsKey(entry.Slot))
            {
                ResourceHandle<T> loaded = await LoadAsync(key, cancellationToken)
                    .ConfigureAwait(false);
                activity?.SetTag("resource.generation", loaded.Generation);
                activity?.SetTag("resource.reload.propagated", 0);
                ResourcesDiagnostics.ReloadOperations.Add(
                    1,
                    new KeyValuePair<string, object?>("outcome", "loaded"));
                ResourcesDiagnostics.ReloadPropagation.Record(0);
                return loaded;
            }

            int reloadedCount = await ReloadSlotsAsync(
                    [entry.Slot],
                    cancellationToken)
                .ConfigureAwait(false);

            ResourceHandle<T> handle = new(key, this, entry.Slot);
            int propagated = reloadedCount - 1;
            activity?.SetTag("resource.generation", handle.Generation);
            activity?.SetTag("resource.reload.propagated", propagated);
            ResourcesDiagnostics.ReloadOperations.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "succeeded"));
            ResourcesDiagnostics.ReloadPropagation.Record(propagated);
            return handle;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", exception.GetType().FullName);
            ResourcesDiagnostics.ReloadOperations.Add(
                1,
                new("outcome", "failed"),
                new("error.type", exception.GetType().Name));
            throw;
        }
    }

    internal async ValueTask<int> ReloadChangedAssetAsync(
        AssetChange change,
        CancellationToken cancellationToken)
    {
        uint[] rootSlots = keys.Find(change.Scheme, change.Address)
            .Select(entry => entry.Slot)
            .Where(resources.ContainsKey)
            .ToArray();
        return rootSlots.Length == 0
            ? 0
            : await ReloadSlotsAsync(rootSlots, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<int> ReloadSlotsAsync(
        IEnumerable<uint> rootSlots,
        CancellationToken cancellationToken)
    {
        uint[] reloadOrder = GetDependentReloadOrder(rootSlots);
        SemaphoreSlim[] acquiredLocks = await AcquireReloadLocksAsync(
            reloadOrder,
            cancellationToken).ConfigureAwait(false);
        var candidates = new Dictionary<uint, IResourceRecord>();
        IResourceRecord[] previous = new IResourceRecord[reloadOrder.Length];
        try
        {
            for (int index = 0; index < reloadOrder.Length; index++)
            {
                uint slot = reloadOrder[index];
                IResourceRecord current = await resources[slot].Value
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                previous[index] = current;
                candidates.Add(
                    slot,
                    await current
                        .ReloadAsync(this, candidates, cancellationToken)
                        .ConfigureAwait(false));
            }

            lock (generationLock)
            {
                foreach ((uint slot, IResourceRecord candidate) in candidates)
                {
                    Lazy<Task<IResourceRecord>> replacement = new(
                        () => Task.FromResult(candidate),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                    _ = replacement.Value;
                    resources[slot] = replacement;
                }
            }

            foreach (IResourceRecord oldRecord in previous)
            {
                await oldRecord.ReleaseAsync().ConfigureAwait(false);
            }

            ResourcesDiagnostics.ReloadedResources.Add(candidates.Count);
            return candidates.Count;
        }
        catch
        {
            foreach (IResourceRecord candidate in candidates.Values.Reverse())
            {
                await candidate.ReleaseAsync().ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            for (int index = acquiredLocks.Length - 1; index >= 0; index--)
            {
                acquiredLocks[index].Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Task<IResourceRecord>[] pending = resources.Values
            .Where(lazy => lazy.IsValueCreated)
            .Select(lazy => lazy.Value)
            .ToArray();
        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch
        {
            // Failed loads do not own a completed resource.
        }

        IResourceRecord[] current = resources.Values
            .Where(pending => pending.IsValueCreated && pending.Value.IsCompletedSuccessfully)
            .Select(pending => pending.Value.Result)
            .ToArray();
        for (int index = current.Length - 1; index >= 0; index--)
        {
            await current[index].ReleaseAsync().ConfigureAwait(false);
        }

        int loadedResourceCount = resources.Values.Count(
            pending => pending.IsValueCreated && pending.Value.IsCompletedSuccessfully);
        ResourcesDiagnostics.LoadedResources.Add(-loadedResourceCount);

        resources.Clear();
        foreach (SemaphoreSlim reloadLock in reloadLocks.Values)
        {
            reloadLock.Dispose();
        }

        reloadLocks.Clear();
        strongReferences.Clear();
    }

    public async ValueTask<ResourceCollectionReport> CollectAsync(
        ResourceCollectionMode mode = ResourceCollectionMode.Budget,
        CancellationToken cancellationToken = default) =>
        await CollectAsync(mode, slots: null, cancellationToken).ConfigureAwait(false);

    internal async ValueTask<ResourceCollectionReport> CollectUnusedAsync(
        IReadOnlyCollection<uint> slots,
        CancellationToken cancellationToken = default) =>
        await CollectAsync(
            ResourceCollectionMode.AllUnused,
            slots.ToHashSet(),
            cancellationToken).ConfigureAwait(false);

    private async ValueTask<ResourceCollectionReport> CollectAsync(
        ResourceCollectionMode mode,
        IReadOnlySet<uint>? slots,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);

        using Activity? activity = ResourcesDiagnostics.Activities.StartActivity(
            "ResourceStore.Collect",
            ActivityKind.Internal);
        activity?.SetTag("resource.collection.mode", mode.ToString());

        int unloaded = 0;
        bool removed;
        do
        {
            removed = false;
            IResourceRecord[] candidates = resources.Values
                .Where(pending => pending.IsValueCreated && pending.Value.IsCompletedSuccessfully)
                .Select(pending => pending.Value.Result)
                .Where(record => GetStrongReferenceCount(record.Slot) == 0)
                .Where(record => slots is null || slots.Contains(record.Slot))
                .OrderBy(record => record.EvictionPriority)
                .ThenBy(record => record.LastAccessSequence)
                .ToArray();
            foreach (IResourceRecord record in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (mode == ResourceCollectionMode.Budget && !RelievesPressure(record))
                {
                    continue;
                }

                uint slot = record.Slot;
                if (!resources.TryGetValue(slot, out Lazy<Task<IResourceRecord>>? pending))
                {
                    continue;
                }

                bool didRemove;
                lock (generationLock)
                {
                    didRemove = record.ReferenceCount == 1
                        && resources.TryRemove(
                            new KeyValuePair<uint, Lazy<Task<IResourceRecord>>>(slot, pending));
                }

                if (!didRemove)
                {
                    continue;
                }

                await record.ReleaseAsync().ConfigureAwait(false);
                ResourcesDiagnostics.LoadedResources.Add(-1);
                unloaded++;
                removed = true;
            }
        }
        while (removed);

        activity?.SetTag("resource.collection.unloaded", unloaded);
        ResourcesDiagnostics.CollectionOperations.Add(
            1,
            new KeyValuePair<string, object?>("mode", mode.ToString()));
        ResourcesDiagnostics.UnloadedResources.Add(unloaded);
        return new ResourceCollectionReport(unloaded);
    }

    internal uint[] GetDependencyClosure(uint rootSlot)
    {
        List<uint> result = [];
        HashSet<uint> visited = [];
        Add(rootSlot);
        return [.. result];

        void Add(uint slot)
        {
            if (!visited.Add(slot))
            {
                return;
            }

            result.Add(slot);
            if (!resources.TryGetValue(slot, out Lazy<Task<IResourceRecord>>? pending)
                || !pending.IsValueCreated
                || !pending.Value.IsCompletedSuccessfully)
            {
                return;
            }

            foreach (uint dependency in pending.Value.Result.Dependencies.Span)
            {
                Add(dependency);
            }
        }
    }

    public async ValueTask UnloadAsync<T>(
        ResourceId<T> id,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!resources.TryGetValue(id.Slot, out Lazy<Task<IResourceRecord>>? pending))
        {
            return;
        }

        IResourceRecord record = await pending.Value
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (GetStrongReferenceCount(id.Slot) != 0 || record.ReferenceCount != 1)
        {
            throw new ResourceInUseException(
                $"The resource slot '{id.Slot}' is still in use.");
        }

        bool didRemove;
        lock (generationLock)
        {
            didRemove = resources.TryRemove(
                new KeyValuePair<uint, Lazy<Task<IResourceRecord>>>(id.Slot, pending));
        }

        if (didRemove)
        {
            await record.ReleaseAsync().ConfigureAwait(false);
            ResourcesDiagnostics.LoadedResources.Add(-1);
        }
    }

    internal async ValueTask<ResourceRecord<T>> LoadDependencyRecordAsync<T>(
        AssetKey<T> key,
        ResourceLoadPath path,
        IReadOnlyDictionary<uint, IResourceRecord>? candidates,
        ResourceLoadOptions loadOptions,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ResourceKeyEntry entry = keys.GetOrAdd(key);
        if (candidates?.TryGetValue(entry.Slot, out IResourceRecord? candidate) == true)
        {
            return (ResourceRecord<T>)candidate;
        }

        ResourceHandle<T> handle = await LoadAsync(
                key,
                path,
                loadOptions,
                cancellationToken)
            .ConfigureAwait(false);
        return GetCurrent(handle.Id);
    }

    internal int GetDependencyCount<T>(AssetKey<T> key)
        where T : notnull
    {
        ResourceKeyEntry entry = keys.GetOrAdd(key);
        if (!resources.TryGetValue(entry.Slot, out Lazy<Task<IResourceRecord>>? lazy)
            || !lazy.IsValueCreated
            || !lazy.Value.IsCompletedSuccessfully)
        {
            return 0;
        }

        return lazy.Value.Result.DependencyCount;
    }

    internal ResourceRecord<T> GetCurrent<T>(ResourceId<T> id)
        where T : notnull
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (!resources.TryGetValue(id.Slot, out Lazy<Task<IResourceRecord>>? pending)
            || !pending.IsValueCreated
            || !pending.Value.IsCompletedSuccessfully)
        {
            throw new ResourceNotFoundException(
                $"The resource slot '{id.Slot}' is not currently loaded.");
        }

        ResourceRecord<T> record = (ResourceRecord<T>)pending.Value.Result;
        record.Touch(Interlocked.Increment(ref accessSequence));
        return record;
    }

    internal bool TryGetCurrent<T>(ResourceId<T> id, out ResourceRecord<T>? record)
        where T : notnull
    {
        if (disposed == 0
            && resources.TryGetValue(id.Slot, out Lazy<Task<IResourceRecord>>? pending)
            && pending.IsValueCreated
            && pending.Value.IsCompletedSuccessfully)
        {
            record = (ResourceRecord<T>)pending.Value.Result;
            record.Touch(Interlocked.Increment(ref accessSequence));
            return true;
        }

        record = null;
        return false;
    }

    internal void AddStrongReference(uint slot) =>
        strongReferences.AddOrUpdate(slot, 1, static (_, count) => checked(count + 1));

    internal void RemoveStrongReference(uint slot)
    {
        while (strongReferences.TryGetValue(slot, out int count))
        {
            if (count <= 0)
            {
                throw new InvalidOperationException("The resource slot was released too many times.");
            }

            if (count == 1)
            {
                if (strongReferences.TryRemove(
                    new KeyValuePair<uint, int>(slot, count)))
                {
                    return;
                }
            }
            else if (strongReferences.TryUpdate(slot, count - 1, count))
            {
                return;
            }
        }

        throw new InvalidOperationException("The resource slot is not retained.");
    }

    private int GetStrongReferenceCount(uint slot) =>
        strongReferences.TryGetValue(slot, out int count) ? count : 0;

    private bool RelievesPressure(IResourceRecord record)
    {
        foreach (ResourceMemoryCost cost in record.MemoryCosts.Span)
        {
            if (options.MemoryBudgets.TryGetValue(cost.Pool, out long budget)
                && memoryUsage.GetValueOrDefault(cost.Pool) > budget)
            {
                return true;
            }
        }

        return false;
    }

    private void AddMemoryUsage(ReadOnlySpan<ResourceMemoryCost> costs)
    {
        foreach (ResourceMemoryCost cost in costs)
        {
            memoryUsage.AddOrUpdate(
                cost.Pool,
                cost.Bytes,
                (_, value) => checked(value + cost.Bytes));
            ResourcesDiagnostics.MemoryUsage.Add(
                cost.Bytes,
                new KeyValuePair<string, object?>("pool", cost.Pool.Name));
        }
    }

    private void RemoveMemoryUsage(ReadOnlyMemory<ResourceMemoryCost> costs)
    {
        foreach (ResourceMemoryCost cost in costs.Span)
        {
            memoryUsage.AddOrUpdate(
                cost.Pool,
                0,
                (_, value) => checked(value - cost.Bytes));
            ResourcesDiagnostics.MemoryUsage.Add(
                -cost.Bytes,
                new KeyValuePair<string, object?>("pool", cost.Pool.Name));
        }
    }

    private static void ValidateMemoryCosts(ReadOnlySpan<ResourceMemoryCost> costs)
    {
        foreach (ResourceMemoryCost cost in costs)
        {
            if (cost.Bytes < 0)
            {
                throw new ResourceLoadException(
                    "A resource loader reported a negative memory cost.");
            }
        }
    }

    internal async ValueTask<IResourceRecord> LoadNextGenerationAsync<T>(
        AssetKey<T> key,
        uint generation,
        IReadOnlyDictionary<uint, IResourceRecord> candidates,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ResourceKeyEntry entry = keys.GetOrAdd(key);
        ResourceLoadPath path = new(entry.Slot, parent: null);
        return await LoadTrackedCoreAsync<T>(
            key,
            entry,
            path,
            generation,
            candidates,
            new ResourceLoadOptions(),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ResourceHandle<T>> LoadAsync<T>(
        AssetKey<T> key,
        ResourceLoadPath? path,
        ResourceLoadOptions loadOptions,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);

        long started = Stopwatch.GetTimestamp();
        using Activity? activity = ResourcesDiagnostics.Activities.StartActivity(
            "ResourceStore.Load",
            ActivityKind.Internal);
        activity?.SetTag("resource.type", typeof(T).FullName);
        activity?.SetTag("asset.scheme", key.Scheme.ToString());
        ResourcesDiagnostics.ActiveLoads.Add(1);

        ResourceKeyEntry entry = keys.GetOrAdd(key);
        if (path?.Contains(entry.Slot) == true)
        {
            throw new ResourceDependencyCycleException(
                $"Loading '{key}' would create a resource dependency cycle.");
        }

        ResourceLoadPath currentPath = path is null
            ? new ResourceLoadPath(entry.Slot, parent: null)
            : path.Add(entry.Slot);
        ResourceLoadInterest interest = loadInterests.GetOrAdd(
            entry.Slot,
            _ => new ResourceLoadInterest());
        interest.AddWaiter();
        Lazy<Task<IResourceRecord>> candidate = new(
                () => LoadTrackedCoreAsync<T>(
                    key,
                    entry,
                    currentPath,
                    generation: 0,
                    candidates: null,
                    loadOptions,
                    interest.CancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<Task<IResourceRecord>> pending = resources.GetOrAdd(entry.Slot, candidate);
        bool cacheHit = !ReferenceEquals(candidate, pending);
        if (cacheHit
            && (!pending.IsValueCreated || !pending.Value.IsCompleted))
        {
            scheduler.Promote(entry.Slot, loadOptions.Priority);
        }
        activity?.SetTag("resource.cache.hit", cacheHit);

        try
        {
            IResourceRecord untypedRecord = await pending.Value
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            ResourceRecord<T> record = (ResourceRecord<T>)untypedRecord;
            activity?.SetTag("resource.generation", record.Generation);
            ResourcesDiagnostics.LoadRequests.Add(
                1,
                new("outcome", "succeeded"),
                new("cache.hit", cacheHit));
            return new ResourceHandle<T>(key, this, entry.Slot);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", exception.GetType().FullName);
            ResourcesDiagnostics.LoadRequests.Add(
                1,
                new("outcome", "failed"),
                new("cache.hit", cacheHit),
                new("error.type", exception.GetType().Name));
            if (pending.IsValueCreated && pending.Value.IsFaulted)
            {
                resources.TryRemove(entry.Slot, out _);
            }

            throw;
        }
        finally
        {
            if (interest.RemoveWaiter()
                && pending.IsValueCreated
                && pending.Value.IsCompleted)
            {
                loadInterests.TryRemove(
                    new KeyValuePair<uint, ResourceLoadInterest>(entry.Slot, interest));
            }

            ResourcesDiagnostics.ActiveLoads.Add(-1);
            ResourcesDiagnostics.LoadDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("cache.hit", cacheHit));
        }
    }

    private async Task<IResourceRecord> LoadTrackedCoreAsync<T>(
        AssetKey<T> key,
        ResourceKeyEntry entry,
        ResourceLoadPath path,
        uint generation,
        IReadOnlyDictionary<uint, IResourceRecord>? candidates,
        ResourceLoadOptions loadOptions,
        CancellationToken cancellationToken)
        where T : notnull
    {
        try
        {
            return await LoadCoreAsync(
                    key,
                    entry,
                    path,
                    generation,
                    candidates,
                    loadOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            scheduler.CompleteRequest(entry.Slot);
            loadInterests.TryRemove(entry.Slot, out _);
        }
    }

    private async Task<IResourceRecord> LoadCoreAsync<T>(
        AssetKey<T> key,
        ResourceKeyEntry entry,
        ResourceLoadPath path,
        uint generation,
        IReadOnlyDictionary<uint, IResourceRecord>? candidates,
        ResourceLoadOptions loadOptions,
        CancellationToken cancellationToken)
        where T : notnull
    {
        if (!resolvers.TryGetValue(
                entry.Scheme,
                out ResourceResolverRegistration? resolverRegistration))
        {
            throw new AssetResolutionException(
                $"No asset resolver is registered for the '{entry.Scheme}' scheme.");
        }

        if (!loaders.TryGetValue(
                typeof(T),
                out ResourceLoaderRegistration? loaderRegistration))
        {
            throw new ResourceLoaderNotFoundException(
                $"No resource loader is registered for '{typeof(T)}'.");
        }

        if (path.IsDependency)
        {
            return await LoadPipelineAsync(cancellationToken).ConfigureAwait(false);
        }

        return await scheduler.ScheduleAsync(
                entry.Slot,
                loaderRegistration.LoadLane,
                loadOptions.Priority,
                LoadPipelineAsync,
                cancellationToken)
            .ConfigureAwait(false);

        async ValueTask<IResourceRecord> LoadPipelineAsync(CancellationToken pipelineToken)
        {
            IAssetResolver resolver = resolverRegistration.Resolver;
            IResourceLoader loader = loaderRegistration.Loader;
            AssetData data = await RunStageAsync(
                resolverRegistration.OpenLane,
                token => resolver.OpenAsync(entry.Address, token),
                pipelineToken)
                .ConfigureAwait(false);
            await using (data.ConfigureAwait(false))
            {
                ResourceLoadContext context = new(
                    data,
                    entry.Text,
                    entry.SelectorStart,
                    this,
                    path,
                    candidates,
                    loadOptions);

                try
                {
                    T resource = await RunStageAsync(
                            loaderRegistration.LoadLane,
                            token => loader.LoadAsync<T>(context, token),
                            pipelineToken)
                        .ConfigureAwait(false);
                    if (resource is null)
                    {
                        throw new ResourceLoadException(
                            $"The resource loader returned null for '{key}'.");
                    }

                    ResourceMemoryCost[] memoryCosts = [.. loader.Measure(resource)];
                    ValidateMemoryCosts(memoryCosts);
                    AddMemoryUsage(memoryCosts);
                    ResourceRecord<T> record = new(
                        key,
                        entry.Slot,
                        generation,
                        resource,
                        context.TakeDependencies(),
                        memoryCosts,
                        loader.EvictionPriority,
                        dispatcher,
                        loaderRegistration.DisposalLane,
                        RemoveMemoryUsage);
                    record.Touch(Interlocked.Increment(ref accessSequence));

                    if (generation == 0)
                    {
                        ResourcesDiagnostics.LoadedResources.Add(1);
                    }

                    return record;
                }
                catch (ResourceException)
                {
                    await context.ReleaseDependenciesAsync().ConfigureAwait(false);
                    throw;
                }
                catch (Exception exception)
                {
                    await context.ReleaseDependenciesAsync().ConfigureAwait(false);
                    throw new ResourceLoadException(
                        $"The resource '{key}' could not be loaded.",
                        exception);
                }
            }
        }

        async ValueTask<TResult> RunStageAsync<TResult>(
            ResourceExecutionLane lane,
            Func<CancellationToken, ValueTask<TResult>> operation,
            CancellationToken token)
        {
            ValueTask<TResult> DispatchAsync(CancellationToken innerToken) =>
                dispatcher.InvokeAsync(lane, operation, innerToken);
            return path.IsDependency
                ? await DispatchAsync(token).ConfigureAwait(false)
                : await scheduler.RunStageAsync(lane, DispatchAsync, token)
                    .ConfigureAwait(false);
        }
    }

    private uint[] GetDependentReloadOrder(IEnumerable<uint> rootSlots)
    {
        Dictionary<uint, IResourceRecord> current = [];
        foreach ((uint slot, Lazy<Task<IResourceRecord>> pending) in resources)
        {
            if (pending.IsValueCreated && pending.Value.IsCompletedSuccessfully)
            {
                current.Add(slot, pending.Value.Result);
            }
        }

        HashSet<uint> affected = [];
        Queue<uint> pendingSlots = new();
        foreach (uint rootSlot in rootSlots)
        {
            pendingSlots.Enqueue(rootSlot);
        }
        while (pendingSlots.TryDequeue(out uint slot))
        {
            if (!affected.Add(slot))
            {
                continue;
            }

            foreach ((uint candidateSlot, IResourceRecord candidate) in current)
            {
                if (candidate.Dependencies.Span.Contains(slot))
                {
                    pendingSlots.Enqueue(candidateSlot);
                }
            }
        }

        List<uint> order = new(affected.Count);
        HashSet<uint> ordered = [];
        while (order.Count < affected.Count)
        {
            bool progressed = false;
            foreach (uint slot in affected)
            {
                if (ordered.Contains(slot))
                {
                    continue;
                }

                ReadOnlySpan<uint> dependencies = current[slot].Dependencies.Span;
                bool hasPendingDependency = false;
                foreach (uint dependency in dependencies)
                {
                    if (affected.Contains(dependency) && !ordered.Contains(dependency))
                    {
                        hasPendingDependency = true;
                        break;
                    }
                }

                if (hasPendingDependency)
                {
                    continue;
                }

                ordered.Add(slot);
                order.Add(slot);
                progressed = true;
            }

            if (!progressed)
            {
                throw new ResourceDependencyCycleException(
                    "The resource dependency graph contains a cycle.");
            }
        }

        return [.. order];
    }

    private async ValueTask<SemaphoreSlim[]> AcquireReloadLocksAsync(
        IEnumerable<uint> slots,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim[] locks = slots
            .Order()
            .Select(slot => reloadLocks.GetOrAdd(slot, _ => new SemaphoreSlim(1, 1)))
            .ToArray();
        int acquired = 0;
        try
        {
            foreach (SemaphoreSlim reloadLock in locks)
            {
                await reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired++;
            }

            return locks;
        }
        catch
        {
            for (int index = acquired - 1; index >= 0; index--)
            {
                locks[index].Release();
            }

            throw;
        }
    }
}
