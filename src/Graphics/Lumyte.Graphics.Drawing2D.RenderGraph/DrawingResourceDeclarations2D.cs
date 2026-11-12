using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

internal sealed class DrawingResourceDeclarations2D(RenderGraphPass pass, RenderTargetBinding2D target, DrawingGraphBindings2D bindings) : IDrawingResourceDeclarations2D
{
    private readonly Dictionary<object, Declaration> _declarations = new(ReferenceEqualityComparer.Instance);

    internal IDrawingResourceDeclarations2D Prepared => new PreparedDeclarations(this);

    public void UseTexture(IGraphicsTexture texture, TextureState state, BarrierScope scope)
    {
        RenderGraphTexture resource = bindings.GetTexture(texture);
        if (ReferenceEquals(resource, target.Texture))
        {
            throw new ArgumentException("A drawing batch cannot read its active attachment.", nameof(texture));
        }

        Add(texture, scope, actual => pass.UseTexture(resource, state, actual));
    }

    public void UseBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope)
        where T : unmanaged => Add(buffer, scope, actual => pass.UseBuffer(bindings.GetBuffer(buffer), actual));

    public void UseShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
        where T : struct, IShaderData => Add(buffer, scope, actual => pass.UseShaderDataBuffer(bindings.GetShaderDataBuffer(buffer), actual));

    internal void Apply()
    {
        foreach (Declaration declaration in _declarations.Values)
        {
            declaration.Apply(declaration.Scope);
        }
    }

    private void Add(object allocation, BarrierScope scope, Action<BarrierScope> apply)
    {
        if (_declarations.TryGetValue(allocation, out Declaration? existing))
        {
            existing.Scope = new(existing.Scope.Stages | scope.Stages, existing.Scope.Access | scope.Access);
        }
        else
        {
            _declarations.Add(allocation, new(scope, apply));
        }
    }

    private void PreparedBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope)
        where T : unmanaged => Add(buffer, scope, actual => pass.UseLocalBuffer(buffer, actual));

    private void PreparedShaderData<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
        where T : struct, IShaderData => Add(buffer, scope, actual => pass.UseLocalShaderDataBuffer(buffer, actual));

    private sealed class Declaration(BarrierScope scope, Action<BarrierScope> apply)
    {
        internal BarrierScope Scope { get; set; } = scope;

        internal Action<BarrierScope> Apply { get; } = apply;
    }

    private sealed class PreparedDeclarations(DrawingResourceDeclarations2D owner) : IDrawingResourceDeclarations2D
    {
        public void UseTexture(IGraphicsTexture texture, TextureState state, BarrierScope scope) => throw new InvalidOperationException("Prepared-root declarations describe only module-owned GPU buffers.");

        public void UseBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope)
            where T : unmanaged => owner.PreparedBuffer(buffer, scope);

        public void UseShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
            where T : struct, IShaderData => owner.PreparedShaderData(buffer, scope);
    }
}
