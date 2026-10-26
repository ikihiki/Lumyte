namespace Lumyte.Graphics.RenderGraph;

/// <summary>Shares explicitly registered extension factories and their optional owners across frame graphs.</summary>
/// <remarks>Configure before creating a graph. Serialize configuration and disposal; frozen factories may serve separate graphs concurrently.</remarks>
public sealed class RenderGraphExtensions : IDisposable
{
    private readonly Dictionary<RuntimeTypeHandle, Func<RenderGraph, object>> _factories = [];
    private List<IDisposable>? _owners;
    private bool _frozen;
    private bool _closed;
    private bool _disposing;

    /// <summary>Registers a factory producing one cached state per graph.</summary>
    /// <typeparam name="T">The exact extension state type.</typeparam>
    /// <param name="factory">Creates graph-local state without reflection or implicit service resolution.</param>
    /// <param name="owner">Optional ownership transferred on successful registration and released with this configuration.</param>
    /// <returns>The same configuration.</returns>
    /// <remarks>The state itself is borrowed. Register its GPU or resource ownership with graph.Retain before preparation.</remarks>
    public RenderGraphExtensions Add<T>(Func<RenderGraph, T> factory, IDisposable? owner = null)
        where T : class
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_frozen)
        {
            throw new InvalidOperationException("Configure extensions before attaching the first graph.");
        }

        if (!_factories.TryAdd(typeof(T).TypeHandle, factory))
        {
            throw new ArgumentException("An extension for this state type is already registered.", nameof(factory));
        }

        if (owner is not null && !(_owners?.Any(existing => ReferenceEquals(existing, owner)) ?? false))
        {
            (_owners ??= []).Add(owner);
        }

        return this;
    }

    /// <summary>Releases registered owners in reverse order, retaining a failed owner for retry.</summary>
    /// <remarks>Already issued graph lifetimes belong to their graphs or compiled plans.</remarks>
    public void Dispose()
    {
        if (_disposing)
        {
            throw new InvalidOperationException("Extension owner disposal cannot be reentered.");
        }

        _closed = true;
        _disposing = true;
        try
        {
            if (_owners is { } owners)
            {
                while (owners.Count != 0)
                {
                    owners[^1].Dispose();
                    owners.RemoveAt(owners.Count - 1);
                }
            }

            _factories.Clear();
        }
        finally
        {
            _disposing = false;
        }
    }

    internal void Attach()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _frozen = true;
    }

    internal Func<RenderGraph, object>? Find(RuntimeTypeHandle type)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        return _factories.GetValueOrDefault(type);
    }
}
