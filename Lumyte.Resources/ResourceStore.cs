using System.Collections.Concurrent;
using System.Diagnostics;

namespace Lumyte.Resources;

/// <summary>Coordinates resource loading, caching, dependencies, and lifetime.</summary>
public sealed class ResourceStore : IAsyncDisposable
{
    private readonly IReadOnlyDictionary<string, IAssetResolver> resolvers;
    private readonly IReadOnlyDictionary<Type, IResourceLoader> loaders;
    private readonly ResourceKeyTable keys = new();
    private readonly ConcurrentDictionary<uint, Lazy<Task<IResourceRecord>>> resources = new();
    private readonly ConcurrentDictionary<uint, SemaphoreSlim> reloadLocks = new();
    private readonly List<IResourceRecord> completedResources = [];
    private readonly Lock completedResourcesLock = new();
    private int disposed;

    public ResourceStore(
        IEnumerable<IAssetResolver> resolvers,
        IEnumerable<IResourceLoader> loaders)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        ArgumentNullException.ThrowIfNull(loaders);

        Dictionary<string, IAssetResolver> registeredResolvers = new(StringComparer.Ordinal);
        foreach (IAssetResolver resolver in resolvers)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            string scheme = AssetKey.NormalizeScheme(resolver.Scheme);
            if (!registeredResolvers.TryAdd(scheme, resolver))
            {
                throw new ArgumentException(
                    $"An asset resolver is already registered for the '{scheme}' scheme.",
                    nameof(resolvers));
            }
        }

        Dictionary<Type, IResourceLoader> registeredLoaders = [];
        foreach (IResourceLoader loader in loaders)
        {
            ArgumentNullException.ThrowIfNull(loader);
            Type resourceType = loader.ResourceType;
            if (!registeredLoaders.TryAdd(resourceType, loader))
            {
                throw new ArgumentException(
                    $"A resource loader is already registered for '{resourceType}'.",
                    nameof(loaders));
            }
        }

        this.resolvers = registeredResolvers;
        this.loaders = registeredLoaders;
    }

    internal int InternedKeyCount => keys.Count;

    public ValueTask<ResourceHandle<T>> LoadAsync<T>(
        AssetKey<T> key,
        CancellationToken cancellationToken = default)
        where T : notnull =>
        LoadAsync(key, path: null, cancellationToken);

    /// <summary>Captures the currently loaded generations as one stable view.</summary>
    public ResourceSnapshot CreateSnapshot()
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);

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

            uint[] reloadOrder = GetDependentReloadOrder(entry.Slot);
            foreach (uint slot in reloadOrder)
            {
                await ReloadCurrentAsync(slot, cancellationToken).ConfigureAwait(false);
            }

            ResourceHandle<T> handle = new(key, this, entry.Slot);
            int propagated = reloadOrder.Length - 1;
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

        IResourceRecord[] completed;
        lock (completedResourcesLock)
        {
            completed = [.. completedResources];
            completedResources.Clear();
        }

        for (int index = completed.Length - 1; index >= 0; index--)
        {
            await completed[index].DisposeAsync().ConfigureAwait(false);
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
    }

    internal ValueTask<ResourceHandle<T>> LoadDependencyAsync<T>(
        AssetKey<T> key,
        ResourceLoadPath path,
        CancellationToken cancellationToken)
        where T : notnull =>
        LoadAsync(key, path, cancellationToken);

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

        return (ResourceRecord<T>)pending.Value.Result;
    }

    internal async ValueTask<IResourceRecord> LoadNextGenerationAsync<T>(
        AssetKey<T> key,
        uint generation,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ResourceKeyEntry entry = keys.GetOrAdd(key);
        ResourceLoadPath path = new(entry.Slot, parent: null);
        return await LoadCoreAsync<T>(
            key,
            entry,
            path,
            generation,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ResourceHandle<T>> LoadAsync<T>(
        AssetKey<T> key,
        ResourceLoadPath? path,
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
        Lazy<Task<IResourceRecord>> candidate = new(
                () => LoadCoreAsync<T>(
                    key,
                    entry,
                    currentPath,
                    generation: 0,
                    CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<Task<IResourceRecord>> pending = resources.GetOrAdd(entry.Slot, candidate);
        bool cacheHit = !ReferenceEquals(candidate, pending);
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
            ResourcesDiagnostics.ActiveLoads.Add(-1);
            ResourcesDiagnostics.LoadDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("cache.hit", cacheHit));
        }
    }

    private async Task<IResourceRecord> LoadCoreAsync<T>(
        AssetKey<T> key,
        ResourceKeyEntry entry,
        ResourceLoadPath path,
        uint generation,
        CancellationToken cancellationToken)
        where T : notnull
    {
        if (!resolvers.TryGetValue(entry.Scheme, out IAssetResolver? resolver))
        {
            throw new AssetResolutionException(
                $"No asset resolver is registered for the '{entry.Scheme}' scheme.");
        }

        if (!loaders.TryGetValue(typeof(T), out IResourceLoader? loader))
        {
            throw new ResourceLoaderNotFoundException(
                $"No resource loader is registered for '{typeof(T)}'.");
        }

        await using AssetData data = await resolver
            .OpenAsync(entry.Address, cancellationToken)
            .ConfigureAwait(false);
        ResourceLoadContext context = new(
            data,
            entry.Text,
            entry.SelectorStart,
            this,
            path);

        try
        {
            T resource = await loader
                .LoadAsync<T>(context, cancellationToken)
                .ConfigureAwait(false);
            if (resource is null)
            {
                throw new ResourceLoadException(
                    $"The resource loader returned null for '{key}'.");
            }

            ResourceRecord<T> record = new(
                key,
                entry.Slot,
                generation,
                resource,
                context.GetDependencies());
            lock (completedResourcesLock)
            {
                completedResources.Add(record);
            }

            if (generation == 0)
            {
                ResourcesDiagnostics.LoadedResources.Add(1);
            }

            return record;
        }
        catch (ResourceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ResourceLoadException(
                $"The resource '{key}' could not be loaded.",
                exception);
        }
    }

    private uint[] GetDependentReloadOrder(uint rootSlot)
    {
        Dictionary<uint, IResourceRecord> current = [];
        foreach ((uint slot, Lazy<Task<IResourceRecord>> pending) in resources)
        {
            if (pending.IsValueCreated && pending.Value.IsCompletedSuccessfully)
            {
                current.Add(slot, pending.Value.Result);
            }
        }

        List<uint> order = [];
        HashSet<uint> visited = [];
        AddDependents(rootSlot);
        return [.. order];

        void AddDependents(uint slot)
        {
            if (!visited.Add(slot))
            {
                return;
            }

            order.Add(slot);
            foreach ((uint candidateSlot, IResourceRecord candidate) in current)
            {
                if (candidate.Dependencies.Span.Contains(slot))
                {
                    AddDependents(candidateSlot);
                }
            }
        }
    }

    private async ValueTask ReloadCurrentAsync(
        uint slot,
        CancellationToken cancellationToken)
    {
        using Activity? activity = ResourcesDiagnostics.Activities.StartActivity(
            "ResourceStore.ReloadResource",
            ActivityKind.Internal);
        activity?.SetTag("resource.slot", slot);
        SemaphoreSlim reloadLock = reloadLocks.GetOrAdd(slot, _ => new SemaphoreSlim(1, 1));
        await reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!resources.TryGetValue(slot, out Lazy<Task<IResourceRecord>>? current))
            {
                return;
            }

            IResourceRecord currentRecord = await current.Value
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            IResourceRecord replacementRecord = await currentRecord
                .ReloadAsync(this, cancellationToken)
                .ConfigureAwait(false);
            activity?.SetTag("resource.generation", replacementRecord.Generation);
            Lazy<Task<IResourceRecord>> replacement = new(
                () => Task.FromResult(replacementRecord),
                LazyThreadSafetyMode.ExecutionAndPublication);
            _ = replacement.Value;
            resources[slot] = replacement;
            ResourcesDiagnostics.ReloadedResources.Add(1);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", exception.GetType().FullName);
            throw;
        }
        finally
        {
            reloadLock.Release();
        }
    }
}
