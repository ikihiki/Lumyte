using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Identifies a non-owning, whole-allocation resource in one graph.</summary>
public abstract class RenderGraphResource
{
    private protected RenderGraphResource(RenderGraph owner, object? allocation, TextureState state, BarrierScope scope, string name)
    {
        Owner = owner;
        Name = name;
        Allocation = allocation;
        InitialState = state;
        InitialScope = scope;
    }

    /// <summary>Gets the diagnostic resource name.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the compiled plan owns the allocation.</summary>
    public bool IsTransient => Allocation is null;

    internal abstract string Kind { get; }

    internal virtual bool InitiallyInitialized => !IsTransient;

    internal RenderGraph Owner { get; }

    internal object? Allocation { get; }

    internal TextureState InitialState { get; }

    internal BarrierScope InitialScope { get; }

    internal abstract IDisposable Create(IGraphicDevice device);

    internal abstract void Validate(TextureState state, BarrierScope scope);

    internal abstract void Barrier(IGraphicsCommandBuffer commands, object allocation, TextureState beforeState, TextureState afterState, BarrierScope before, BarrierScope after);
}
