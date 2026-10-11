using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Maps borrowed image allocations to their unique handles in one render graph.</summary>
public sealed class DrawingGraphBindings2D
{
    private readonly Dictionary<IGraphicsTexture, RenderGraphTexture> _textures = new(ReferenceEqualityComparer.Instance);

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

    internal DrawingGraphBindings2D Snapshot()
    {
        var result = new DrawingGraphBindings2D(Graph);
        foreach (KeyValuePair<IGraphicsTexture, RenderGraphTexture> pair in _textures)
        {
            result._textures.Add(pair.Key, pair.Value);
        }

        return result;
    }
}
