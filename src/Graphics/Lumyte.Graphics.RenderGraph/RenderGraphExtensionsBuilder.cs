namespace Lumyte.Graphics.RenderGraph;

/// <summary>Registers graph extensions and prepares their shared resources in a single asynchronous build.</summary>
/// <remarks>
/// Serialize builder calls. A build consumes the builder; use a new builder for another configuration.
/// Keep the builder to retry Dispose if a custom owner's cleanup fails during an unsuccessful build.
/// </remarks>
public sealed class RenderGraphExtensionsBuilder : IDisposable
{
    private Dictionary<RuntimeTypeHandle, Func<RenderGraph, object>>? _factories = [];
    private List<Preparation>? _preparations;
    private List<IDisposable>? _owners;
    private bool _consumed;
    private bool _building;
    private bool _disposed;
    private bool _disposing;

    /// <summary>Registers a factory producing one cached state per graph.</summary>
    /// <typeparam name="T">The exact extension state type.</typeparam>
    /// <param name="factory">Creates graph-local state without reflection or implicit service resolution.</param>
    /// <param name="owner">Ownership transferred on successful registration, then to the built configuration.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>The state itself is borrowed. Register its GPU or resource ownership with graph.Retain before preparation.</remarks>
    public RenderGraphExtensionsBuilder Add<T>(Func<RenderGraph, T> factory, IDisposable? owner = null)
        where T : class
    {
        EnsureConfiguring();
        if (!_factories!.TryAdd(typeof(T).TypeHandle, factory))
        {
            throw new ArgumentException("An extension for this state type is already registered.", nameof(factory));
        }

        Retain(owner);
        return this;
    }

    /// <summary>Defers shared preparation until BuildAsync, reserving the state type immediately.</summary>
    /// <typeparam name="T">The exact extension state type.</typeparam>
    /// <param name="prepare">Produces a factory and optional owner; release any partial resources before throwing.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>Preparations run in registration order. A returned owner transfers even when cancellation is requested before the callback completes.</remarks>
    public RenderGraphExtensionsBuilder AddAsync<T>(Func<CancellationToken, ValueTask<RenderGraphExtensionFactory<T>>> prepare)
        where T : class
    {
        EnsureConfiguring();
        RuntimeTypeHandle type = typeof(T).TypeHandle;
        if (!_factories!.TryAdd(type, static _ => throw new InvalidOperationException("The extension has not been prepared.")))
        {
            throw new ArgumentException("An extension for this state type is already registered.", nameof(prepare));
        }

        (_preparations ??= []).Add(new(type, async cancellationToken =>
        {
            RenderGraphExtensionFactory<T> prepared = await prepare(cancellationToken).ConfigureAwait(false);
            return new(prepared.Factory, prepared.Owner);
        }));
        return this;
    }

    /// <summary>Prepares every registered extension and transfers ownership to an immutable configuration.</summary>
    /// <param name="cancellationToken">Cancels preparation; already acquired resources are released before completion.</param>
    /// <returns>A reusable configuration whose graph-local factories perform no asynchronous loading.</returns>
    /// <remarks>
    /// Failure or cancellation consumes the builder and releases owners in reverse acquisition order. If cleanup also fails,
    /// both failures are reported and Dispose can retry the failed owner and its remaining dependencies.
    /// An already canceled token is rejected before consuming the builder.
    /// </remarks>
    public async ValueTask<RenderGraphExtensions> BuildAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfiguring();
        cancellationToken.ThrowIfCancellationRequested();
        _consumed = true;
        _building = true;
        try
        {
            if (_preparations is { } preparations)
            {
                foreach (Preparation preparation in preparations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PreparedFactory prepared = await preparation.Prepare(cancellationToken).ConfigureAwait(false);
                    Retain(prepared.Owner);
                    _factories![preparation.Type] = prepared.Factory;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var extensions = new RenderGraphExtensions(_factories!, _owners);
            _factories = null;
            _owners = null;
            return extensions;
        }
        catch (Exception failure)
        {
            try
            {
                ReleaseOwners();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Extension preparation and cleanup both failed; dispose the builder to retry cleanup.", failure, cleanupFailure);
            }

            throw;
        }
        finally
        {
            _preparations = null;
            _factories = null;
            _building = false;
        }
    }

    /// <summary>Releases owners not transferred to a successful build, preserving failed cleanup for retry.</summary>
    public void Dispose()
    {
        if (_building || _disposing)
        {
            throw new InvalidOperationException("The builder cannot be disposed during preparation or owner cleanup.");
        }

        _disposed = true;
        _disposing = true;
        try
        {
            ReleaseOwners();
        }
        finally
        {
            _preparations = null;
            _factories = null;
            _disposing = false;
        }
    }

    private void EnsureConfiguring()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_consumed)
        {
            throw new InvalidOperationException("The builder has already started a build.");
        }
    }

    private void Retain(IDisposable? owner)
    {
        if (owner is null)
        {
            return;
        }

        if (_owners is { } owners)
        {
            foreach (IDisposable existing in owners)
            {
                if (ReferenceEquals(existing, owner))
                {
                    return;
                }
            }
        }

        (_owners ??= []).Add(owner);
    }

    private void ReleaseOwners()
    {
        if (_owners is { } owners)
        {
            while (owners.Count != 0)
            {
                owners[^1].Dispose();
                owners.RemoveAt(owners.Count - 1);
            }
        }
    }

    private readonly record struct PreparedFactory(Func<RenderGraph, object> Factory, IDisposable? Owner);

    private readonly record struct Preparation(RuntimeTypeHandle Type, Func<CancellationToken, ValueTask<PreparedFactory>> Prepare);
}
