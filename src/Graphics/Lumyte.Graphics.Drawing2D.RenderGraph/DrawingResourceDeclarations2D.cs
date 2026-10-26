using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

internal sealed class DrawingResourceDeclarations2D(RenderGraphPass pass, RenderTargetBinding2D target, DrawingGraphBindings2D bindings) : IDrawingResourceDeclarations2D
{
    /// <inheritdoc/>
    public void UseTexture(IGraphicsTexture texture, TextureState state, BarrierScope scope)
    {
        RenderGraphTexture resource = bindings.GetTexture(texture);
        if (ReferenceEquals(resource, target.Texture))
        {
            throw new ArgumentException("A drawing batch cannot read its active attachment.", nameof(texture));
        }

        pass.UseTexture(resource, state, scope);
    }

    /// <inheritdoc/>
    public void UseBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope)
        where T : unmanaged => pass.UseBuffer(bindings.GetBuffer(buffer), scope);

    /// <inheritdoc/>
    public void UseShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
        where T : struct, IShaderData => pass.UseShaderDataBuffer(bindings.GetShaderDataBuffer(buffer), scope);
}
