using System.Collections.Concurrent;

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

        ResourceKeyEntry entry = keys.GetOrAdd(key);
        SemaphoreSlim reloadLock = reloadLocks.GetOrAdd(entry.Slot, _ => new SemaphoreSlim(1, 1));
        await reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            uint generation = 0;
            if (resources.TryGetValue(entry.Slot, out Lazy<Task<IResourceRecord>>? current))
            {
                IResourceRecord currentRecord = await current.Value
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                generation = checked(((ResourceRecord<T>)currentRecord).Generation + 1);
            }

            ResourceLoadPath path = new(entry.Slot, parent: null);
            IResourceRecord untypedRecord = await LoadCoreAsync<T>(
                key,
                entry,
                path,
                generation).ConfigureAwait(false);
            ResourceRecord<T> record = (ResourceRecord<T>)untypedRecord;
            Lazy<Task<IResourceRecord>> replacement = new(
                () => Task.FromResult(untypedRecord),
                LazyThreadSafetyMode.ExecutionAndPublication);
            _ = replacement.Value;
            resources[entry.Slot] = replacement;
            return new ResourceHandle<T>(key, this, entry.Slot);
        }
        finally
        {
            reloadLock.Release();
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

    private async ValueTask<ResourceHandle<T>> LoadAsync<T>(
        AssetKey<T> key,
        ResourceLoadPath? path,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);

        ResourceKeyEntry entry = keys.GetOrAdd(key);
        if (path?.Contains(entry.Slot) == true)
        {
            throw new ResourceDependencyCycleException(
                $"Loading '{key}' would create a resource dependency cycle.");
        }

        ResourceLoadPath currentPath = path is null
            ? new ResourceLoadPath(entry.Slot, parent: null)
            : path.Add(entry.Slot);
        Lazy<Task<IResourceRecord>> pending = resources.GetOrAdd(
            entry.Slot,
            _ => new Lazy<Task<IResourceRecord>>(
                () => LoadCoreAsync<T>(key, entry, currentPath, generation: 0),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            IResourceRecord untypedRecord = await pending.Value
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            ResourceRecord<T> record = (ResourceRecord<T>)untypedRecord;
            return new ResourceHandle<T>(key, this, entry.Slot);
        }
        catch
        {
            if (pending.IsValueCreated && pending.Value.IsFaulted)
            {
                resources.TryRemove(entry.Slot, out _);
            }

            throw;
        }
    }

    private async Task<IResourceRecord> LoadCoreAsync<T>(
        AssetKey<T> key,
        ResourceKeyEntry entry,
        ResourceLoadPath path,
        uint generation)
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
            .OpenAsync(entry.Address)
            .ConfigureAwait(false);
        ResourceLoadContext context = new(
            data,
            entry.Text,
            entry.SelectorStart,
            this,
            path);

        try
        {
            T resource = await loader.LoadAsync<T>(context).ConfigureAwait(false);
            if (resource is null)
            {
                throw new ResourceLoadException(
                    $"The resource loader returned null for '{key}'.");
            }

            ResourceRecord<T> record = new(
                entry.Slot,
                generation,
                resource,
                context.GetDependencies());
            lock (completedResourcesLock)
            {
                completedResources.Add(record);
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
}
