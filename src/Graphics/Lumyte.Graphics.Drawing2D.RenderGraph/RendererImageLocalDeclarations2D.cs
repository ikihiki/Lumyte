using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

internal sealed class RendererImageLocalDeclarations2D(Lumyte.Graphics.RenderGraph.RenderGraph graph, RenderGraphPass pass, IDrawingResourceDeclarations2D external, RendererImageImports2D imports) : IDrawingResourceDeclarations2D
{
    public void UseTexture(IGraphicsTexture texture, TextureState state, BarrierScope scope) => external.UseTexture(texture, state, scope);

    public void UseTextureView(IGraphicsTextureView view, BarrierScope scope)
    {
        imports.UseTextureView(view, scope);
        RenderGraphTexture texture = imports.Bindings.GetTexture(view.Texture);
        pass.UseNativeView(texture, view, scope);
    }

    public void UseSampler(IGraphicsSampler sampler) => pass.UseSampler(sampler);

    public void UseBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope)
        where T : unmanaged => pass.UseBuffer(graph.PreparationBuffer(buffer), scope);

    public void UseBuffer<T>(BufferSlice<T> range, BarrierScope scope)
        where T : unmanaged => pass.UsePreparationBuffer(range, scope);

    public void UseShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
        where T : struct, IShaderArguments => pass.UseShaderDataBuffer(graph.PreparationShaderData(buffer), scope);

    public void UseShaderDataBuffer<T>(ShaderDataSlice<T> range, BarrierScope scope)
        where T : struct, IShaderArguments => pass.UsePreparationShaderData(range, scope);
}
