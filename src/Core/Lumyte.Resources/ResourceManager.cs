using Lumyte.Memory;

namespace Lumyte.Resources;

/// <summary>Owns pipeline generations, tracked dependencies and atomic publication within one DI scope.</summary>
public sealed partial class ResourceManager : IAsyncDisposable, IMemoryReclaimer
{
    private static long _nextManagerId;
    private readonly object _gate = new();
    private readonly string _name;
    private readonly IResourceSource _source;
    private readonly IMemoryManager _memory;
    private readonly Dictionary<RuntimeTypeHandle, ResourcePipeline> _pipelines;
    private readonly Dictionary<string, ResourceRuntimeState> _runtimes = new(StringComparer.Ordinal);
    private readonly Dictionary<ResourceNode, ResourceMetadata> _metadata = [];
    private readonly HashSet<ResourceGeneration> _generations = [];
    private readonly Dictionary<ResourceNode, ResourceLoad> _loads = [];
    private readonly List<ResourceSlot> _slots = [];
    private readonly Dictionary<ResourceNode, uint> _slotByKey = [];
    private readonly HashSet<ResourceNode> _invalidated = [];
    private readonly HashSet<IAsyncDisposable> _sessions = [];
    private readonly HashSet<IAsyncDisposable> _failedSessions = [];
    private readonly SemaphoreSlim _maintenance = new(1, 1);
    private readonly IDisposable _reclaimerRegistration;
    private Dictionary<ResourceNode, ResourceGeneration> _published = [];
    private ResourceBuildView? _normalView;
    private Exception? _runtimeFailure;
    private Task? _disposeTask;
    private ulong _publicationVersion;
    private ulong _revision;
    private int _externalOwners;
    private int _operations;
    private bool _closing;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ResourceManager"/> class with borrowed DI services.</summary>
    /// <param name="name">The diagnostic manager name.</param>
    /// <param name="source">The borrowed input source.</param>
    /// <param name="pipelines">The immutable, dependency-injected pipeline configuration.</param>
    /// <param name="memory">The shared memory budget manager.</param>
    /// <param name="runtimeDependencies">The borrowed runtime generation providers.</param>
    public ResourceManager(string name, IResourceSource source, IEnumerable<ResourcePipeline> pipelines, IMemoryManager memory, IEnumerable<IResourceRuntimeDependency>? runtimeDependencies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pipelines);
        ArgumentNullException.ThrowIfNull(memory);
        _name = name;
        _source = source;
        _memory = memory;
        _pipelines = pipelines.ToDictionary(static pipeline => pipeline.OutputTypeHandle);
        ValidatePipelines();
        ManagerId = NextManagerId();
        try
        {
            foreach (IResourceRuntimeDependency provider in runtimeDependencies ?? [])
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(provider.Id);
                ResourceRuntimeSnapshot snapshot = provider.Snapshot;
                ValidateRuntimeSnapshot(default, snapshot);
                _runtimes.Add(provider.Id, new(provider, provider.Identity, snapshot));
            }

            foreach (ResourceRuntimeState runtime in _runtimes.Values)
            {
                runtime.Handler = (_, _) => ObserveRuntime(runtime);
                runtime.Provider.Changed += runtime.Handler;
                RefreshRuntime(runtime);
            }

            _reclaimerRegistration = memory.RegisterReclaimer(this);
        }
        catch
        {
            UnsubscribeRuntimes();
            throw;
        }
    }

    /// <summary>Gets the process-local identity, never reused by another manager.</summary>
    public ulong ManagerId { get; }

    /// <summary>Gets the version of the currently published resource map.</summary>
    public ulong PublicationVersion
    {
        get
        {
            lock (_gate)
            {
                return _publicationVersion;
            }
        }
    }

    /// <summary>Acquires a revision-fixed lease, sharing compatible in-flight builds.</summary>
    /// <typeparam name="T">The registered output type.</typeparam>
    /// <param name="key">The requested logical resource.</param>
    /// <param name="cancellationToken">Cancels this wait without cancelling shared construction.</param>
    /// <returns>A lease owned by the caller.</returns>
    public async ValueTask<ResourceLease<T>> AcquireAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResourceNode node = GetNode(key);
        RefreshRuntimes();
        ResourceLoad load;
        bool start = false;
        lock (_gate)
        {
            EnsureRunning();
            if (_published.TryGetValue(node, out ResourceGeneration? cached))
            {
                ThrowIfUnavailable(cached);
                return CreateLease<T>(cached);
            }

            ThrowIfKnownUnavailable(node);
            if (!_loads.TryGetValue(node, out load!))
            {
                ResourceBuildView view = _normalView is { } existing && existing.PublicationVersion == _publicationVersion
                    ? existing
                    : CreateView([], CancellationToken.None);
                _normalView = view;
                view.Roots++;
                load = new(view);
                _loads.Add(node, load);
                _operations++;
                start = true;
            }

            load.Waiters++;
            _operations++;
        }

        if (start)
        {
            _ = RunLoadAsync(node, load);
        }

        try
        {
            ResourceGeneration generation = await load.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            RefreshRuntimes();
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfUnavailable(generation);
                return CreateLease<T>(generation);
            }
        }
        finally
        {
            lock (_gate)
            {
                load.Waiters--;
                if (load.Generation is { } generation)
                {
                    generation.Pins--;
                }

                _operations--;
            }
        }
    }

    /// <summary>Creates logical residency and a non-owning handle that follows later publications.</summary>
    /// <typeparam name="T">The registered output type.</typeparam>
    /// <param name="key">The requested resource.</param>
    /// <param name="cancellationToken">Cancels the initial acquisition wait.</param>
    /// <returns>The managed owner of the handle.</returns>
    public async ValueTask<ResourceReference<T>> CreateReferenceAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default)
        where T : class
    {
        using ResourceLease<T> lease = await AcquireAsync(key, cancellationToken).ConfigureAwait(false);
        ResourceNode node = GetNode(key);
        lock (_gate)
        {
            EnsureRunning();
            if (!_slotByKey.TryGetValue(node, out uint index))
            {
                index = checked((uint)_slots.Count);
                for (int candidate = 0; candidate < _slots.Count; candidate++)
                {
                    ResourceSlot old = _slots[candidate];
                    if (old.References == 0 && old.Generation < uint.MaxValue)
                    {
                        index = (uint)candidate;
                        break;
                    }
                }

                uint generation = index == _slots.Count ? 1 : checked(_slots[(int)index].Generation + 1);
                ResourceSlot slot = new(node, generation);
                if (index == _slots.Count)
                {
                    _slots.Add(slot);
                }
                else
                {
                    _slots[(int)index] = slot;
                }

                _slotByKey.Add(node, index);
            }

            ResourceSlot current = _slots[(int)index];
            current.Current = _published.GetValueOrDefault(node);
            current.References++;
            if (current.Current is { } resident)
            {
                resident.Pins++;
            }

            _externalOwners++;
            ResourceHandle<T> handle = new(ManagerId, index, current.Generation);
            return new(key, handle, () => IsHandleAvailable(handle), () => ReturnReference(handle));
        }
    }

    /// <summary>Acquires the handle's current generation without starting I/O or pipeline work.</summary>
    /// <typeparam name="T">The handle's registered output type.</typeparam>
    /// <param name="handle">The non-owning logical handle.</param>
    /// <param name="lease">The acquired generation, or null when unavailable.</param>
    /// <returns>Whether a current usable generation was acquired.</returns>
    public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceLease<T>? lease)
        where T : class
    {
        RefreshRuntimes();
        lock (_gate)
        {
            EnsureRunning();
            return TryAcquireFrom(handle, _published, out lease);
        }
    }

    /// <summary>Pins one publication map so multiple handle reads observe the same generations.</summary>
    /// <returns>A read scope owned by the caller.</returns>
    public ResourceReadScope BeginRead()
    {
        lock (_gate)
        {
            EnsureRunning();
            Dictionary<ResourceNode, ResourceGeneration> map = _published;
            foreach (ResourceGeneration generation in map.Values)
            {
                generation.Pins++;
            }

            _externalOwners++;
            return new(_publicationVersion, new ReadView(this, map), () => ReturnReadView(map));
        }
    }

    /// <summary>Opens an independent bounded streaming cursor pinned to one factory revision.</summary>
    /// <typeparam name="TResource">The registered immutable stream factory type.</typeparam>
    /// <typeparam name="TElement">The unmanaged decoded element type.</typeparam>
    /// <param name="key">The stream factory key.</param>
    /// <param name="options">The bounded buffering configuration.</param>
    /// <param name="cancellationToken">Cancels creation only.</param>
    /// <returns>An initialized session with an independently owned cursor.</returns>
    public async ValueTask<ResourceStreamSession<TElement>> OpenStreamAsync<TResource, TElement>(ResourceKey<TResource> key, ResourceStreamOptions options, CancellationToken cancellationToken = default)
        where TResource : class, IResourceStream<TElement>
        where TElement : unmanaged
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate<TElement>();
        ResourceLease<TResource> lease = await AcquireAsync(key, cancellationToken).ConfigureAwait(false);
        IMemoryScope? memory = null;
        ResourceStreamSession<TElement>? session = null;
        try
        {
            memory = _memory.CreateScope($"{_name}/stream/{typeof(TResource)}/{lease.Revision}");
            session = new(memory, lease, lease.Revision, options, () => ReturnSession(session!));
            lock (_gate)
            {
                _sessions.Add(session);
            }

            await session.InitializeAsync(lease.Value, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch (Exception error)
        {
            try
            {
                if (session is not null)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    if (memory is not null)
                    {
                        await memory.DisposeAsync().ConfigureAwait(false);
                    }

                    lease.Dispose();
                }
            }
            catch (Exception cleanupError)
            {
                if (session is not null)
                {
                    lock (_gate)
                    {
                        _failedSessions.Add(session);
                    }
                }

                throw new AggregateException(error, cleanupError);
            }

            throw;
        }
    }

    private static ulong NextManagerId()
    {
        while (true)
        {
            long previous = Volatile.Read(ref _nextManagerId);
            long next = checked(previous + 1);
            if (Interlocked.CompareExchange(ref _nextManagerId, next, previous) == previous)
            {
                return (ulong)next;
            }
        }
    }

    private ResourceNode GetNode<T>(ResourceKey<T> key)
        where T : class
    {
        if (key.Address.Id.Value is null || key.Address.Subresource is null || key.Address.Variant is null)
        {
            throw new ArgumentException("Supply an initialized typed resource key.", nameof(key));
        }

        if (!_pipelines.ContainsKey(key.TypeHandle))
        {
            throw new NotSupportedException($"A resource pipeline for '{typeof(T)}' is not registered.");
        }

        return new(key.Address, key.TypeHandle);
    }

    private void ValidatePipelines()
    {
        foreach (ResourcePipeline pipeline in _pipelines.Values)
        {
            HashSet<RuntimeTypeHandle> visited = [];
            ResourcePipeline current = pipeline;
            while (current.InputType is { } input)
            {
                if (!visited.Add(current.OutputTypeHandle))
                {
                    throw new ArgumentException($"Static resource pipeline cycle includes '{current.OutputType}'.", nameof(pipeline));
                }

                if (!_pipelines.TryGetValue(input.TypeHandle, out ResourcePipeline? dependency))
                {
                    throw new ArgumentException($"Pipeline for '{current.OutputType}' has an unregistered input '{input}'.", nameof(pipeline));
                }

                current = dependency;
            }
        }
    }

    private void EnsureRunning()
    {
        ObjectDisposedException.ThrowIf(_closing || _disposed, this);
        if (_runtimeFailure is { } failure)
        {
            throw new InvalidOperationException("A resource runtime provider violated its version contract.", failure);
        }
    }

    private ResourceLease<T> CreateLease<T>(ResourceGeneration generation)
        where T : class
    {
        generation.Pins++;
        _externalOwners++;
        ResourceKey<T> key = new(generation.Node.Address);
        return new(key, generation.Revision, (T)generation.Value!, () => GetUnavailableThreadSafe(generation), () => ReturnLease(generation));
    }

    private bool TryAcquireFrom<T>(ResourceHandle<T> handle, Dictionary<ResourceNode, ResourceGeneration> map, out ResourceLease<T>? lease)
        where T : class
    {
        lease = null;
        if (!TryGetSlot(handle, out ResourceSlot? slot)
            || !map.TryGetValue(slot!.Node, out ResourceGeneration? generation)
            || generation.Releasing || generation.Released || GetUnavailable(generation) is not null)
        {
            return false;
        }

        lease = CreateLease<T>(generation);
        return true;
    }

    private bool TryGetSlot<T>(ResourceHandle<T> handle, out ResourceSlot? slot)
        where T : class
    {
        slot = null;
        if (handle.ManagerId != ManagerId || handle.Generation == 0 || handle.Slot >= _slots.Count)
        {
            return false;
        }

        ResourceSlot candidate = _slots[(int)handle.Slot];
        if (candidate.References == 0 || candidate.Generation != handle.Generation || !candidate.Node.TypeHandle.Equals(typeof(T).TypeHandle))
        {
            return false;
        }

        slot = candidate;
        return true;
    }

    private bool IsHandleAvailable<T>(ResourceHandle<T> handle)
        where T : class
    {
        RefreshRuntimes();
        lock (_gate)
        {
            EnsureRunning();
            return TryGetSlot(handle, out ResourceSlot? slot) && slot!.Current is { } current && GetUnavailable(current) is null;
        }
    }

    private void ReturnReference<T>(ResourceHandle<T> handle)
        where T : class
    {
        lock (_gate)
        {
            ResourceSlot slot = _slots[(int)handle.Slot];
            slot.References--;
            if (slot.Current is { } current)
            {
                current.Pins--;
            }

            if (slot.References == 0)
            {
                _slotByKey.Remove(slot.Node);
                slot.Current = null;
            }

            _externalOwners--;
        }
    }

    private void ReturnLease(ResourceGeneration generation)
    {
        lock (_gate)
        {
            generation.Pins--;
            _externalOwners--;
        }
    }

    private void ReturnReadView(Dictionary<ResourceNode, ResourceGeneration> map)
    {
        lock (_gate)
        {
            foreach (ResourceGeneration generation in map.Values)
            {
                generation.Pins--;
            }

            _externalOwners--;
        }
    }

    private void ReturnSession(IAsyncDisposable session)
    {
        lock (_gate)
        {
            _sessions.Remove(session);
            _failedSessions.Remove(session);
        }
    }

    private sealed class ReadView(ResourceManager manager, Dictionary<ResourceNode, ResourceGeneration> map) : IResourceReadView
    {
        public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceLease<T>? lease)
            where T : class
        {
            manager.RefreshRuntimes();
            lock (manager._gate)
            {
                manager.EnsureRunning();
                return manager.TryAcquireFrom(handle, map, out lease);
            }
        }
    }
}
