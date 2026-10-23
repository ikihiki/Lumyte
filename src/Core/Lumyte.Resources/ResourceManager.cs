using Lumyte.Memory;

namespace Lumyte.Resources;

/// <summary>Owns pipeline generations, tracked dependencies and atomic publication within its configured service lifetime.</summary>
public sealed partial class ResourceManager : IAsyncDisposable, IMemoryReclaimer
{
    private readonly object _gate = new();
    private readonly string _name;
    private readonly IResourceSource _source;
    private readonly IMemoryManager _memory;
    private readonly Dictionary<RuntimeTypeHandle, IResourceStage> _stages;
    private readonly Dictionary<ResourceNode, ResourceMetadata> _metadata = [];
    private readonly HashSet<ResourceGeneration> _generations = [];
    private readonly Dictionary<ResourceNode, ResourceLoad> _loads = [];
    private readonly SemaphoreSlim _maintenance = new(1, 1);
    private readonly IDisposable _reclaimerRegistration;
    private Dictionary<ResourceNode, ResourceGeneration> _published = [];
    private ResourceBuildView? _normalView;
    private Task? _disposeTask;
    private ulong _publicationVersion;
    private ulong _revision;
    private int _externalOwners;
    private int _operations;
    private bool _closing;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ResourceManager"/> class with borrowed source, stage and memory services.</summary>
    /// <param name="name">The diagnostic manager name.</param>
    /// <param name="source">The borrowed input source.</param>
    /// <param name="stages">The borrowed source stages and transforms, with one producer per output type.</param>
    /// <param name="memory">The shared memory budget manager.</param>
    public ResourceManager(string name, IResourceSource source, IEnumerable<IResourceStage> stages, IMemoryManager memory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        _source = source;
        _memory = memory;
        _stages = stages.Select(ValidateStage).ToDictionary(static stage => stage.OutputType.TypeHandle);
        ValidatePipelines();
        _reclaimerRegistration = memory.RegisterReclaimer(this);
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
        ResourceLoad load;
        bool start = false;
        lock (_gate)
        {
            EnsureRunning();
            if (_published.TryGetValue(node, out ResourceGeneration? cached))
            {
                return CreateLease<T>(cached);
            }

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
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
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

    private static IResourceStage ValidateStage(IResourceStage stage)
    {
        if (stage.OutputType.IsValueType || stage.OutputType.ContainsGenericParameters
            || (stage.InputType is { } input && (input.IsValueType || input.ContainsGenericParameters)))
        {
            throw new ArgumentException("Stage input and output types must be closed reference types.", nameof(stage));
        }

        return stage;
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
        foreach (IResourceStage pipeline in _stages.Values)
        {
            HashSet<RuntimeTypeHandle> visited = [];
            IResourceStage current = pipeline;
            while (current.InputType is { } input)
            {
                if (!visited.Add(current.OutputType.TypeHandle))
                {
                    throw new ArgumentException($"Static resource pipeline cycle includes '{current.OutputType}'.", nameof(pipeline));
                }

                if (!_stages.TryGetValue(input.TypeHandle, out IResourceStage? dependency))
                {
                    throw new ArgumentException($"Pipeline for '{current.OutputType}' has an unregistered input '{input}'.", nameof(pipeline));
                }

                current = dependency;
            }
        }
    }

    private void EnsureRunning() => ObjectDisposedException.ThrowIf(_closing || _disposed, this);

    private ResourceLease<T> CreateLease<T>(ResourceGeneration generation)
        where T : class
    {
        generation.Pins++;
        _externalOwners++;
        ResourceKey<T> key = new(generation.Node.Address);
        return new(key, generation.Revision, (T)generation.Value!, () => ReturnLease(generation));
    }

    private void ReturnLease(ResourceGeneration generation)
    {
        lock (_gate)
        {
            generation.Pins--;
            _externalOwners--;
        }
    }
}
