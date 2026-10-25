using Lumyte.Memory;

namespace Lumyte.Resources;

/// <summary>Provides tracked dependencies and owned cleanup for one pipeline construction.</summary>
/// <remarks>
/// Finish synchronous context calls before the stage returns. Registered asynchronous dependencies and sources
/// are drained by the manager; unjoined work must not continue calling the context after the stage ends.
/// </remarks>
public sealed class ResourceBuildContext
{
    private readonly ResourceManager _manager;
    private readonly ResourceBuildView _view;
    private readonly ResourceGeneration _generation;
    private readonly ResourceBuildContext _pipeline;
    private SynchronizationContext? _jobContext;
    private bool _pipelineActive;
    private bool _closed;

    internal ResourceBuildContext(ResourceManager manager, ResourceBuildView view, ResourceGeneration generation, ResourceBuildContext? parent = null)
    {
        _manager = manager;
        _view = view;
        _generation = generation;
        _pipeline = parent?._pipeline ?? this;
        InlineDepth = parent is null ? 0 : parent.InlineDepth + 1;
    }

    /// <summary>Gets the requested element and variant.</summary>
    public ResourceAddress Address => _generation.Node.Address;

    /// <summary>Gets the selected environment for this stage and output generation.</summary>
    /// <remarks>Acquire current device epochs through this stable runtime; do not retain a device from an earlier build.</remarks>
    public IResourceRuntime Runtime => _generation.Runtime;

    /// <summary>Gets the runtime identity of the exact output type being constructed.</summary>
    public RuntimeTypeHandle OutputTypeHandle => _generation.Node.TypeHandle;

    internal int InlineDepth { get; set; }

    internal bool CanSharePipeline => Volatile.Read(ref _pipeline._pipelineActive)
        && _pipeline._jobContext is not null && ReferenceEquals(SynchronizationContext.Current, _pipeline._jobContext);

    internal bool ShouldYield => !_closed && CanSharePipeline && _generation.Executor.ShouldYield;

    internal ResourceGeneration Generation => _generation;

    internal bool IsClosed => _closed;

    /// <summary>Runs the manager-owned stage procedure inside the executor's job.</summary>
    /// <param name="cancellationToken">The manager-owned build cancellation token.</param>
    /// <returns>Completion of the stage, its registered dependencies and cleanup.</returns>
    /// <remarks>Executors call this once. The manager owns output recording, failure propagation and cleanup.</remarks>
    public ValueTask ExecuteAsync(CancellationToken cancellationToken)
        => _manager.ExecuteStageAsync(this, cancellationToken);

    /// <summary>Requires a shared dependency and records its lifetime and provenance.</summary>
    /// <typeparam name="T">The exact registered output type.</typeparam>
    /// <param name="key">The required logical resource.</param>
    /// <param name="lifetime">Whether its value is retained for the build or output lifetime.</param>
    /// <param name="cancellationToken">Cancels this dependency wait.</param>
    /// <returns>The borrowed dependency value.</returns>
    public ValueTask<T> RequireAsync<T>(ResourceKey<T> key, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, CancellationToken cancellationToken = default)
        where T : class
    {
        EnsureOpen();
        return _manager.RequireAsync(_view, _generation, this, key, lifetime, cancellationToken);
    }

    /// <summary>Opens an observed input and records its source dependency.</summary>
    /// <param name="id">The source-relative resource.</param>
    /// <param name="lifetime">How long to keep the snapshot alive.</param>
    /// <param name="cancellationToken">Cancels this snapshot wait.</param>
    /// <returns>The snapshot borrowed from the manager.</returns>
    public ValueTask<ResourceSourceSnapshot> OpenSourceAsync(ResourceId id, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Build, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        return _manager.OpenSourceAsync(_view, _generation, id, lifetime, cancellationToken);
    }

    /// <summary>Transfers ownership of an auxiliary cleanup operation to the construction.</summary>
    /// <param name="cleanup">An idempotent, retryable cleanup callback.</param>
    /// <param name="lifetime">The build or output lifetime of the auxiliary resource.</param>
    public void RegisterCleanup(Func<ValueTask> cleanup, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Build)
    {
        EnsureOpen();
        _manager.RegisterCleanup(_generation, cleanup, lifetime);
    }

    /// <summary>Transfers ownership of a synchronous disposable without allocating a cleanup delegate.</summary>
    /// <param name="cleanup">An idempotent, retryable disposable resource.</param>
    /// <param name="lifetime">The build or output lifetime of the auxiliary resource.</param>
    public void RegisterCleanup(IDisposable cleanup, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Build)
    {
        EnsureOpen();
        _manager.RegisterCleanup(_generation, cleanup, lifetime);
    }

    /// <summary>Gets the borrowed memory scope for the requested construction lifetime.</summary>
    /// <param name="lifetime">The build or output lifetime.</param>
    /// <returns>A scope whose disposal belongs to the manager.</returns>
    public IMemoryScope GetMemoryScope(ResourceDependencyLifetime lifetime)
    {
        EnsureOpen();
        return _manager.GetMemoryScope(_generation, lifetime);
    }

    /// <summary>Records the exact runtime version used to construct this output.</summary>
    /// <param name="dependencyId">The registered runtime name.</param>
    /// <param name="expectedVersion">The version captured by the stage.</param>
    /// <param name="lifetime">Whether runtime loss also invalidates the completed value.</param>
    /// <param name="providerIdentity">An optional identity that must match the registered provider.</param>
    public void TrackRuntimeDependency(string dependencyId, ulong expectedVersion, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, object? providerIdentity = null)
    {
        EnsureOpen();
        _manager.TrackRuntimeDependency(_generation, dependencyId, expectedVersion, lifetime, providerIdentity);
    }

    internal void BeginExecution()
    {
        EnsureOpen();
        if (_pipelineActive)
        {
            throw new InvalidOperationException("A build context can execute its stage only once.");
        }

        _jobContext = SynchronizationContext.Current;
        Volatile.Write(ref _pipelineActive, true);
    }

    internal void EndExecution() => Volatile.Write(ref _pipelineActive, false);

    internal void Close() => _closed = true;

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(_closed, this);
}
