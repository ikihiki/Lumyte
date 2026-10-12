using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>Maps borrowed image allocations to their unique handles in one render graph.</summary>
public sealed class DrawingGraphBindings2D
{
    private readonly Dictionary<IGraphicsTexture, RenderGraphTexture> _textures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, RenderGraphResource> _buffers = new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes a new instance of the <see cref="DrawingGraphBindings2D"/> class.</summary>
    /// <param name="graph">The graph to which all handles belong.</param>
    public DrawingGraphBindings2D(RenderGraphBuilder graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Graph = graph;
    }

    internal RenderGraphBuilder Graph { get; }

    /// <summary>Binds an allocation; repeated identical bindings are permitted.</summary>
    /// <param name="texture">The borrowed allocation.</param>
    /// <param name="resource">The allocation's unique handle in this graph.</param>
    public void BindTexture(IGraphicsTexture texture, RenderGraphTexture resource)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(resource);
        if (_textures.TryGetValue(texture, out RenderGraphTexture? existing))
        {
            if (!ReferenceEquals(existing, resource))
            {
                throw new ArgumentException("The allocation is already bound to a different resource.", nameof(resource));
            }

            return;
        }

        if (_textures.Values.Any(value => ReferenceEquals(value, resource)))
        {
            throw new ArgumentException("The resource is already bound to a different allocation.", nameof(resource));
        }

        _textures.Add(texture, resource);
    }

    /// <summary>Gets the handle bound to an image's parent allocation.</summary>
    /// <param name="texture">The borrowed allocation.</param>
    /// <returns>The graph-local handle.</returns>
    public RenderGraphTexture GetTexture(IGraphicsTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return _textures.TryGetValue(texture, out RenderGraphTexture? resource)
            ? resource
            : throw new InvalidOperationException("The image allocation has not been bound to this graph.");
    }

    /// <summary>Binds an external buffer to its existing graph handle.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="buffer">The borrowed allocation.</param>
    /// <param name="resource">The graph-local handle.</param>
    public void BindBuffer<T>(IGraphicsBuffer<T> buffer, RenderGraphBuffer<T> resource)
        where T : unmanaged => BindBufferResource(buffer, resource);

    /// <summary>Binds external shader data to its existing graph handle.</summary>
    /// <typeparam name="T">The shader data type.</typeparam>
    /// <param name="buffer">The borrowed allocation.</param>
    /// <param name="resource">The graph-local handle.</param>
    public void BindShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, RenderGraphShaderDataBuffer<T> resource)
        where T : struct, IShaderData => BindBufferResource(buffer, resource);

    /// <summary>Gets the handle bound to an external buffer.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="buffer">The borrowed allocation.</param>
    /// <returns>The existing graph handle.</returns>
    public RenderGraphBuffer<T> GetBuffer<T>(IGraphicsBuffer<T> buffer)
        where T : unmanaged => _buffers.TryGetValue(buffer, out RenderGraphResource? resource) && resource is RenderGraphBuffer<T> typed
            ? typed : throw new InvalidOperationException("The buffer has not been bound to this graph.");

    /// <summary>Gets the handle bound to external shader data.</summary>
    /// <typeparam name="T">The shader data type.</typeparam>
    /// <param name="buffer">The borrowed allocation.</param>
    /// <returns>The existing graph handle.</returns>
    public RenderGraphShaderDataBuffer<T> GetShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer)
        where T : struct, IShaderData => _buffers.TryGetValue(buffer, out RenderGraphResource? resource) && resource is RenderGraphShaderDataBuffer<T> typed
            ? typed : throw new InvalidOperationException("The shader data has not been bound to this graph.");

    internal DrawingGraphBindings2D Snapshot()
    {
        var result = new DrawingGraphBindings2D(Graph);
        foreach (KeyValuePair<IGraphicsTexture, RenderGraphTexture> pair in _textures)
        {
            result._textures.Add(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<object, RenderGraphResource> pair in _buffers)
        {
            result._buffers.Add(pair.Key, pair.Value);
        }

        return result;
    }

    private void BindBufferResource(object allocation, RenderGraphResource resource)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentNullException.ThrowIfNull(resource);
        if (_buffers.TryGetValue(allocation, out RenderGraphResource? existing) && !ReferenceEquals(existing, resource))
        {
            throw new ArgumentException("The buffer is already bound to a different resource.", nameof(resource));
        }

        if (_buffers.Any(pair => !ReferenceEquals(pair.Key, allocation) && ReferenceEquals(pair.Value, resource)))
        {
            throw new ArgumentException("The resource is already bound to a different buffer.", nameof(resource));
        }

        _buffers[allocation] = resource;
    }
}
