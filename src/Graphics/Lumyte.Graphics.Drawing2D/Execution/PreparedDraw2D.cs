using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Owns a compiled private drawing graph and its preparation resources.</summary>
public sealed class PreparedDraw2D : IDisposable
{
    private readonly CompiledRenderGraph _graph;
    private readonly PreparedDrawingPasses2D _preparation;
    private bool _disposed;
    private bool _recorded;

    internal PreparedDraw2D(CompiledRenderGraph graph, PreparedDrawingPasses2D preparation)
    {
        _graph = graph;
        _preparation = preparation;
    }

    /// <summary>Records once into an idle recording command buffer without submitting it.</summary>
    /// <param name="commands">The borrowed command buffer from the rendering device.</param>
    public void Record(IGraphicsCommandBuffer commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recorded)
        {
            throw new InvalidOperationException("Prepared drawing can be recorded only once.");
        }

        _recorded = true;
        _graph.Record(commands);
    }

    /// <summary>Releases the graph and preparation after GPU completion and command disposal.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _graph.Dispose();
        _disposed = true;
        _preparation.Dispose();
    }
}
