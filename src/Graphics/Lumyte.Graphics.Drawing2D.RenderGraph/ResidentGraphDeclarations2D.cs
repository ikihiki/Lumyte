using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

// Resolve the complete borrowed declaration set before adding an executable callback.
internal sealed class ResidentGraphDeclarations2D(RenderTargetBinding2D target, DrawingGraphBindings2D bindings) : IDrawingResourceDeclarations2D
{
    private readonly Dictionary<object, Declaration> _declarations = new(ReferenceEqualityComparer.Instance);
    private readonly List<Action<RenderGraphPass>> _bindings = [];

    public void UseTexture(IGraphicsTexture texture, TextureState state, BarrierScope scope)
    {
        RenderGraphTexture resource = Texture(texture);
        RenderGraphValidation.Scope(scope, required: true);
        resource.Validate(state, scope);
        Add(texture, state, scope, (pass, actual) => pass.UseTexture(resource, state, actual));
    }

    public void UseTextureView(IGraphicsTextureView view, BarrierScope scope)
    {
        RenderGraphTexture resource = Texture(view.Texture);
        RenderGraphValidation.Scope(scope, required: true);
        resource.Validate(TextureState.Sampled, scope);
        _bindings.Add(pass => pass.UseNativeView(resource, view, scope));
    }

    public void UseBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope)
        where T : unmanaged
    {
        RenderGraphBuffer<T> resource = bindings.GetBuffer(buffer);
        Validate(resource, scope);
        Add(buffer, TextureState.Undefined, scope, (pass, actual) => pass.UseBuffer(resource, actual));
    }

    public void UseShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
        where T : struct, IShaderArguments
    {
        RenderGraphShaderDataBuffer<T> resource = bindings.GetShaderDataBuffer(buffer);
        Validate(resource, scope);
        Add(buffer, TextureState.Undefined, scope, (pass, actual) => pass.UseShaderDataBuffer(resource, actual));
    }

    public void UseSampler(IGraphicsSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        _bindings.Add(pass => pass.UseSampler(sampler));
    }

    internal void Apply(RenderGraphPass pass)
    {
        foreach (Declaration declaration in _declarations.Values)
        {
            declaration.Apply(pass, declaration.Scope);
        }

        foreach (Action<RenderGraphPass> apply in _bindings)
        {
            apply(pass);
        }
    }

    private void Add(object allocation, TextureState state, BarrierScope scope, Action<RenderGraphPass, BarrierScope> apply)
    {
        if (_declarations.TryGetValue(allocation, out Declaration? existing))
        {
            if (existing.State != state)
            {
                throw new ArgumentException("Resident declarations for one allocation must use one combined state.");
            }

            existing.Scope = new(existing.Scope.Stages | scope.Stages, existing.Scope.Access | scope.Access);
        }
        else
        {
            _declarations.Add(allocation, new(state, scope, apply));
        }
    }

    private RenderGraphTexture Texture(IGraphicsTexture allocation)
    {
        RenderGraphTexture resource = bindings.GetTexture(allocation);
        if (!ReferenceEquals(resource.Owner, bindings.Graph) || !ReferenceEquals(resource.Allocation, allocation) || ReferenceEquals(resource, target.Texture))
        {
            throw new ArgumentException("Resident resources require exact external allocations from the parent graph, distinct from its target.");
        }

        return resource;
    }

    private void Validate(RenderGraphResource resource, BarrierScope scope)
    {
        if (!ReferenceEquals(resource.Owner, bindings.Graph) || resource.IsTransient)
        {
            throw new ArgumentException("Resident resources require imported parent-graph allocations.");
        }

        RenderGraphValidation.Scope(scope, required: true);
        resource.Validate(TextureState.Undefined, scope);
    }

    private sealed class Declaration(TextureState state, BarrierScope scope, Action<RenderGraphPass, BarrierScope> apply)
    {
        internal TextureState State { get; } = state;

        internal BarrierScope Scope { get; set; } = scope;

        internal Action<RenderGraphPass, BarrierScope> Apply { get; } = apply;
    }
}
