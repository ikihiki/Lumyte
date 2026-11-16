using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

internal sealed class DrawingResourceDeclarations2D(RenderGraphPass pass, RenderTargetBinding2D target, DrawingGraphBindings2D bindings) : IDrawingResourceDeclarations2D
{
    private readonly Dictionary<object, Declaration> _declarations = new(ReferenceEqualityComparer.Instance);

    private readonly List<Declaration> _bindingDeclarations = [];

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
        where T : struct, IShaderArguments => Add(buffer, scope, actual => pass.UseShaderDataBuffer(bindings.GetShaderDataBuffer(buffer), actual));

    public void UseTextureView(IGraphicsTextureView view, BarrierScope scope)
    {
        RenderGraphTexture resource = bindings.GetTexture(view.Texture);
        if (ReferenceEquals(resource, target.Texture))
        {
            throw new ArgumentException("A drawing batch cannot sample its active target.");
        }

        _bindingDeclarations.Add(new(scope, actual => pass.UseNativeView(resource, view, actual)));
    }

    public void UseSampler(IGraphicsSampler sampler)
        => _bindingDeclarations.Add(new(default, _ => pass.UseSampler(sampler)));

    internal void Apply()
    {
        foreach (Declaration declaration in _declarations.Values.Concat(_bindingDeclarations))
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
        where T : struct, IShaderArguments => Add(buffer, scope, actual => pass.UseLocalShaderDataBuffer(buffer, actual));

    private void PreparedBuffer<T>(BufferSlice<T> range, BarrierScope scope)
        where T : unmanaged => _bindingDeclarations.Add(new(scope, actual => pass.UseLocalBuffer(range, actual)));

    private void PreparedShaderData<T>(ShaderDataSlice<T> range, BarrierScope scope)
        where T : struct, IShaderArguments => _bindingDeclarations.Add(new(scope, actual => pass.UseLocalShaderDataBuffer(range, actual)));

    private sealed class Declaration(BarrierScope scope, Action<BarrierScope> apply)
    {
        internal BarrierScope Scope { get; set; } = scope;

        internal Action<BarrierScope> Apply { get; } = apply;
    }

    private sealed class PreparedDeclarations(DrawingResourceDeclarations2D owner) : IDrawingResourceDeclarations2D
    {
        public void UseBuffer<T>(BufferSlice<T> range, BarrierScope scope)
            where T : unmanaged => owner.PreparedBuffer(range, scope);

        public void UseShaderDataBuffer<T>(ShaderDataSlice<T> range, BarrierScope scope)
            where T : struct, IShaderArguments => owner.PreparedShaderData(range, scope);

        public void UseTexture(IGraphicsTexture texture, TextureState state, BarrierScope scope) => throw new InvalidOperationException("Prepared-root declarations describe only module-owned GPU buffers.");

        public void UseBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope)
            where T : unmanaged => owner.PreparedBuffer(buffer, scope);

        public void UseShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
            where T : struct, IShaderArguments => owner.PreparedShaderData(buffer, scope);
    }
}
