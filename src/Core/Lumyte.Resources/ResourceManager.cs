using Lumyte.Memory;

namespace Lumyte.Resources;

/// <summary>Owns pipeline generations, tracked dependencies and atomic publication within its configured service lifetime.</summary>
public sealed partial class ResourceManager : IAsyncDisposable, IMemoryReclaimer
{
    private static long _nextManagerId;
    private readonly object _gate = new();
    private readonly string _name;
    private readonly IResourceSource _source;
    private readonly IMemoryManager _memory;
    private readonly ResourceWatchTargets _watchTargets = new();
    private readonly ResourceReloadService? _hotReload;
    private readonly Dictionary<RuntimeTypeHandle, (IResourceStage Stage, IResourceRuntime Runtime)> _stages;
    private readonly Dictionary<IResourceRuntime, IResourceExecutor> _executors = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, ResourceRuntimeState> _runtimes = new(StringComparer.Ordinal);
    private readonly Dictionary<ResourceNode, int> _metadataReferences = [];
    private readonly HashSet<ResourceNode> _metadataPruneCandidates = [];
    private readonly Dictionary<ResourceNode, int> _generationCounts = [];
    private readonly HashSet<ResourceGeneration> _failedGenerations = [];
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
    private Dictionary<ResourceNode, ResourceMetadata> _metadata = [];
    private bool _metadataShared;
    private bool _watchRescanRequested;
    private ResourceBuildView? _normalView;
    private Exception? _runtimeFailure;
    private Task? _disposeTask;
    private ulong _publicationVersion;
    private ulong _revision;
    private int _externalOwners;
    private int _operations;
    private int _reloadOperations;
    private bool _closing;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ResourceManager"/> class with borrowed source, stage and memory services.</summary>
    /// <param name="name">The diagnostic manager name.</param>
    /// <param name="source">The borrowed input source.</param>
    /// <param name="stages">The borrowed source stages and transforms, with one producer per output type.</param>
    /// <param name="memory">The shared memory budget manager.</param>
    /// <param name="runtime">The shared default environment for stages without an override.</param>
    /// <param name="runtimeDependencies">The borrowed runtime generation providers.</param>
    /// <param name="stageRuntimes">Optional stage environments, selected by their exact output type and fixed during construction.</param>
    /// <param name="reloadOptions">Batching and retry settings for automatic reload.</param>
    public ResourceManager(string name, IResourceSource source, IEnumerable<IResourceStage> stages, IMemoryManager memory, IResourceRuntime runtime, IEnumerable<IResourceRuntimeDependency>? runtimeDependencies = null, IEnumerable<ResourceStageRuntime>? stageRuntimes = null, ResourceReloadOptions? reloadOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        _source = source;
        _memory = memory;
        _stages = stages.Select(ValidateStage).ToDictionary(static stage => stage.OutputType.TypeHandle, stage => (stage, runtime));
        HashSet<RuntimeTypeHandle>? configured = null;
        foreach (ResourceStageRuntime binding in stageRuntimes ?? [])
        {
            if (binding.OutputType is null || binding.Runtime is null)
            {
                throw new ArgumentException("Supply an initialized stage runtime binding.", nameof(stageRuntimes));
            }

            RuntimeTypeHandle type = binding.OutputType.TypeHandle;
            if (!_stages.TryGetValue(type, out (IResourceStage Stage, IResourceRuntime Runtime) selected))
            {
                throw new ArgumentException($"No stage produces '{binding.OutputType}'.", nameof(stageRuntimes));
            }

            if (!(configured ??= []).Add(type))
            {
                throw new ArgumentException($"A runtime is already selected for '{binding.OutputType}'.", nameof(stageRuntimes));
            }

            _stages[type] = (selected.Stage, binding.Runtime);
        }

        foreach ((IResourceStage stage, IResourceRuntime selectedRuntime) in _stages.Values)
        {
            if (!stage.RuntimeType.IsInstanceOfType(selectedRuntime))
            {
                throw new ArgumentException($"Stage '{stage.GetType()}' requires runtime '{stage.RuntimeType}', but '{selectedRuntime.GetType()}' was selected.", nameof(stageRuntimes));
            }
        }

        ValidatePipelines();
        ManagerId = NextManagerId();
        try
        {
            foreach (IResourceRuntimeDependency provider in runtimeDependencies ?? [])
            {
                RegisterRuntimeProvider(provider);
            }

            foreach ((IResourceStage _, IResourceRuntime selectedRuntime) in _stages.Values)
            {
                if (!_executors.ContainsKey(selectedRuntime))
                {
                    _executors.Add(selectedRuntime, selectedRuntime.Executor);
                    foreach (IResourceRuntimeDependency provider in selectedRuntime.Dependencies)
                    {
                        RegisterRuntimeProvider(provider);
                    }
                }
            }

            foreach (ResourceRuntimeState dependency in _runtimes.Values)
            {
                dependency.Handler = (_, _) => ObserveRuntime(dependency);
                dependency.Provider.Changed += dependency.Handler;
                RefreshRuntime(dependency);
            }

            _hotReload = reloadOptions?.Enabled != false && source.Changes is { } changes
                ? new ResourceReloadService(this, changes, reloadOptions) : null;
            _reclaimerRegistration = memory.RegisterReclaimer(this);
            _hotReload?.Start();
        }
        catch
        {
            UnsubscribeRuntimes();
            throw;
        }
    }

    /// <summary>Gets the manager-owned automatic reload worker, or null when the source has no notifications or automatic reload is disabled.</summary>
    public ResourceReloadService? HotReload => _hotReload;

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

    internal IResourceWatchTargets WatchTargets => _watchTargets;

    /// <summary>Creates logical residency and a non-owning handle that follows later publications.</summary>
    /// <typeparam name="T">The registered output type.</typeparam>
    /// <param name="key">The requested resource.</param>
    /// <param name="cancellationToken">Cancels the initial acquisition wait.</param>
    /// <returns>The managed owner of the handle.</returns>
    public async ValueTask<ResourceReference<T>> AcquireAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default)
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
                return CreateReference<T>(node);
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
                EnsureRunning();
                ThrowIfUnavailable(generation);
                return CreateReference<T>(node);
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

    /// <summary>Acquires an owning reference from a live handle without I/O or pipeline work.</summary>
    /// <typeparam name="T">The resource output type.</typeparam>
    /// <param name="handle">The logical handle to resolve.</param>
    /// <param name="reference">The owning reference, or null when resolution fails.</param>
    /// <returns>Whether a reference was acquired.</returns>
    public bool TryAcquire<T>(ResourceHandle<T> handle, out ResourceReference<T>? reference)
        where T : class
    {
        RefreshRuntimes();
        lock (_gate)
        {
            EnsureRunning();
            reference = null;
            if (!TryGetSlot(handle, out ResourceSlot? slot)
                || !_published.TryGetValue(slot!.Node, out ResourceGeneration? generation)
                || generation.State != ResourceGenerationState.Ready || TryGetUnavailable(generation, out _))
            {
                return false;
            }

            reference = CreateReference(handle, slot);
            return true;
        }
    }

    /// <summary>Captures the published resource generations so multiple acquisitions share one publication.</summary>
    /// <returns>A snapshot retaining all published generations until disposed; no loading or value copying occurs.</returns>
    public ResourceSnapshot CaptureSnapshot()
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
            return new(this, _publicationVersion, map);
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
        options.Validate<TElement>();
        using ResourceReference<TResource> reference = await AcquireAsync(key, cancellationToken).ConfigureAwait(false);
        ResourceLease<TResource> lease = reference.Acquire();
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

    internal ResourceLease<T>? AcquireCurrentLease<T>(ResourceHandle<T> handle)
        where T : class
    {
        RefreshRuntimes();
        lock (_gate)
        {
            EnsureRunning();
            return TryAcquireFrom(handle, _published, out ResourceLease<T>? lease) ? lease : null;
        }
    }

    internal bool IsHandleAvailable<T>(ResourceHandle<T> handle)
        where T : class
    {
        RefreshRuntimes();
        lock (_gate)
        {
            EnsureRunning();
            return TryGetSlot(handle, out ResourceSlot? slot) && slot!.Current is { } current && !TryGetUnavailable(current, out _);
        }
    }

    internal void ReturnReference<T>(ResourceHandle<T> handle)
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
                if (slot.Current is null)
                {
                    _metadataPruneCandidates.Add(slot.Node);
                }

                slot.Current = null;
            }

            _externalOwners--;
        }
    }

    internal void ReturnLease(ResourceGeneration generation)
    {
        lock (_gate)
        {
            generation.Pins--;
            _externalOwners--;
        }
    }

    internal void ReturnReadView(Dictionary<ResourceNode, ResourceGeneration> map)
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

    internal bool TryAcquireFromView<T>(ResourceHandle<T> handle, Dictionary<ResourceNode, ResourceGeneration> map, out ResourceLease<T>? lease)
        where T : class
    {
        RefreshRuntimes();
        lock (_gate)
        {
            EnsureRunning();
            return TryAcquireFrom(handle, map, out lease);
        }
    }

    private static IResourceStage ValidateStage(IResourceStage stage)
    {
        if (stage.OutputType.IsValueType || stage.OutputType.ContainsGenericParameters
            || (stage.InputType is { } input && (input.IsValueType || input.ContainsGenericParameters)))
        {
            throw new ArgumentException("Stage input and output types must be closed reference types.", nameof(stage));
        }

        return stage;
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

        if (!_stages.ContainsKey(key.TypeHandle))
        {
            throw new NotSupportedException($"A resource pipeline for '{typeof(T)}' is not registered.");
        }

        return new(key.Address, key.TypeHandle);
    }

    private void ValidatePipelines()
    {
        foreach ((IResourceStage pipeline, IResourceRuntime _) in _stages.Values)
        {
            HashSet<RuntimeTypeHandle> visited = [];
            IResourceStage current = pipeline;
            while (current.InputType is { } input)
            {
                if (!visited.Add(current.OutputType.TypeHandle))
                {
                    throw new ArgumentException($"Static resource pipeline cycle includes '{current.OutputType}'.", nameof(pipeline));
                }

                if (!_stages.TryGetValue(input.TypeHandle, out (IResourceStage Stage, IResourceRuntime Runtime) dependency))
                {
                    throw new ArgumentException($"Pipeline for '{current.OutputType}' has an unregistered input '{input}'.", nameof(pipeline));
                }

                current = dependency.Stage;
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

    private ResourceReference<T> CreateReference<T>(ResourceNode node)
        where T : class
    {
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
        ResourceHandle<T> handle = new(ManagerId, index, current.Generation);
        return CreateReference(handle, current);
    }

    private ResourceReference<T> CreateReference<T>(ResourceHandle<T> handle, ResourceSlot slot)
        where T : class
    {
        slot.References++;
        if (slot.Current is { } resident)
        {
            resident.Pins++;
        }

        _externalOwners++;
        ResourceKey<T> key = new(slot.Node.Address);
        return new(this, key, handle);
    }

    private ResourceLease<T> CreateLease<T>(ResourceGeneration generation)
        where T : class
    {
        generation.Pins++;
        _externalOwners++;
        return new(this, generation);
    }

    private bool TryAcquireFrom<T>(ResourceHandle<T> handle, Dictionary<ResourceNode, ResourceGeneration> map, out ResourceLease<T>? lease)
        where T : class
    {
        lease = null;
        if (!TryGetSlot(handle, out ResourceSlot? slot)
            || !map.TryGetValue(slot!.Node, out ResourceGeneration? generation)
            || generation.State != ResourceGenerationState.Ready || TryGetUnavailable(generation, out _))
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

    private void ReturnSession(IAsyncDisposable session)
    {
        lock (_gate)
        {
            _sessions.Remove(session);
            _failedSessions.Remove(session);
        }
    }
}
