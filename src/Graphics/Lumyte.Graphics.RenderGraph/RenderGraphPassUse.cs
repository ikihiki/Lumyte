using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

// Closed typed declarations avoid exposing a graph-mutating configuration callback.
internal abstract class RenderGraphPassUse
{
    private RenderGraphPassUse()
    {
    }

    internal static RenderGraphPassUse Texture(RenderGraphTexture resource, TextureState state, BarrierScope scope)
        => new TextureUse(resource, state, scope);

    internal static RenderGraphPassUse Buffer<T>(RenderGraphBuffer<T> resource, ulong offset, ulong count, BarrierScope scope)
        where T : unmanaged => new BufferUse<T>(resource, offset, count, scope);

    internal abstract void Apply(RenderGraph graph, IGraphicDevice device, RenderGraphPass pass);

    private sealed class TextureUse(RenderGraphTexture resource, TextureState state, BarrierScope scope) : RenderGraphPassUse
    {
        internal override void Apply(RenderGraph graph, IGraphicDevice device, RenderGraphPass pass)
        {
            graph.GetImportedTexture(resource, device);
            pass.UseTexture(resource, state, scope);
        }
    }

    private sealed class BufferUse<T>(RenderGraphBuffer<T> resource, ulong offset, ulong count, BarrierScope scope) : RenderGraphPassUse
        where T : unmanaged
    {
        internal override void Apply(RenderGraph graph, IGraphicDevice device, RenderGraphPass pass)
        {
            graph.GetImportedBuffer(resource, offset, count, device);
            pass.UseBuffer(resource, offset, count, scope);
        }
    }
}
