namespace Lumyte.Graphics.RenderGraph;

/// <summary>Shares explicitly registered extension factories and their optional owners across frame graphs.</summary>
/// <remarks>Created by RenderGraphExtensionsBuilder.BuildAsync. Factories may serve separate graphs concurrently; serialize disposal with their use.</remarks>
public sealed class RenderGraphExtensions : IDisposable
{
    private readonly Dictionary<RuntimeTypeHandle, Func<RenderGraph, object>> _factories;
    private readonly List<IDisposable>? _owners;
    private bool _closed;
    private bool _disposing;

    internal RenderGraphExtensions(Dictionary<RuntimeTypeHandle, Func<RenderGraph, object>> factories, List<IDisposable>? owners)
    {
        _factories = factories;
        _owners = owners;
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

    internal void Attach() => ObjectDisposedException.ThrowIf(_closed, this);

    internal Func<RenderGraph, object>? Find(RuntimeTypeHandle type)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        return _factories.GetValueOrDefault(type);
    }
}
