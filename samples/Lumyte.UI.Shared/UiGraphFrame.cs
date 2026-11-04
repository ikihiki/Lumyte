using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Resources;
using Lumyte.Resources.Graphics.Drawing2D;
using Lumyte.Resources.Graphics.Drawing2D.RenderGraph;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.UI.SampleSupport;

internal sealed class UiGraphFrame : IDisposable
{
    private readonly IDisposable _prepared;
    private readonly IDisposable _renderer;
    private bool _preparedReturned;
    private bool _rendererReturned;

    internal UiGraphFrame(IDisposable prepared, IDisposable renderer)
    {
        _prepared = prepared;
        _renderer = renderer;
    }

    public void Dispose()
    {
        if (!_preparedReturned)
        {
            _prepared.Dispose();
            _preparedReturned = true;
        }

        if (!_rendererReturned)
        {
            _renderer.Dispose();
            _rendererReturned = true;
        }
    }

    internal static async ValueTask<UiGraphFrame> AppendAsync(Graph graph, RenderGraphDrawList2D list, RenderTargetBinding2D target, DrawingGraphBindings2D bindings, DrawingCommandRenderers2D renderers, ResourceReference<Renderer2DResource>? reference = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        RenderGraphRenderer2D renderer = reference is null
            ? new(renderers, list, target, bindings)
            : reference.CreateRenderGraphRenderer(renderers, list, target, bindings);
        try
        {
            PreparedDrawingPasses2D prepared = await graph.Append2D(renderer, cancellationToken);
            return new(prepared, renderer);
        }
        catch (Exception failure)
        {
            try
            {
                renderer.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(failure, cleanup);
            }

            throw;
        }
    }
}
