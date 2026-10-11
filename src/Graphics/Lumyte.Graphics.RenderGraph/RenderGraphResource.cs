using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Identifies a non-owning, whole-allocation resource in one graph.</summary>
public abstract class RenderGraphResource
{
    private protected RenderGraphResource(RenderGraph owner, object allocation, TextureState state, BarrierScope scope)
    {
        Owner = owner;
        Allocation = allocation;
        InitialState = state;
        InitialScope = scope;
    }

    internal RenderGraph Owner { get; }

    internal object Allocation { get; }

    internal TextureState InitialState { get; }

    internal BarrierScope InitialScope { get; }

    internal abstract void Validate(TextureState state, BarrierScope scope);

    internal abstract void Barrier(IGraphicsCommandBuffer commands, TextureState beforeState, TextureState afterState, BarrierScope before, BarrierScope after);
}
