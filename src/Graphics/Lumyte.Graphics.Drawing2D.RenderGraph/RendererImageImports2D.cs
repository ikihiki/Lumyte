using Lumyte.Graphics.Abstractions;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

internal sealed class RendererImageImports2D(RenderGraphBuilder graph) : IDrawingResourceDeclarations2D
{
    private readonly HashSet<object> _allocations = new(ReferenceEqualityComparer.Instance);

    internal DrawingGraphBindings2D Bindings { get; } = new(graph);

    public void UseTexture(IGraphicsTexture texture, TextureState state, BarrierScope scope)
    {
        if (_allocations.Add(texture))
        {
            Bindings.BindTexture(texture, graph.GetOrImportTexture(texture, state, scope, "drawing2d.image.input"));
        }
    }

    public void UseTextureView(IGraphicsTextureView view, BarrierScope scope) => UseTexture(view.Texture, TextureState.Sampled, scope);

    public void UseBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope)
        where T : unmanaged
    {
        if (_allocations.Add(buffer))
        {
            Bindings.BindBuffer(buffer, graph.GetOrImportBuffer(buffer, scope));
        }
    }

    public void UseShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
        where T : struct, IShaderArguments
    {
        if (_allocations.Add(buffer))
        {
            Bindings.BindShaderDataBuffer(buffer, graph.GetOrImportShaderDataBuffer(buffer, scope));
        }
    }
}
