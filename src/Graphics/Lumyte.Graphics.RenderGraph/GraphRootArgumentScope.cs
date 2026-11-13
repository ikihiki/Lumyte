using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Infers graph resources once from the final native root reference metadata.</summary>
internal sealed class GraphRootArgumentScope : IShaderArgumentScope
{
    private readonly bool _graphics;
    private RenderGraphContext? _context;
    private IReadOnlyList<RenderGraphTexture>? _outputs;
    private IShaderArgumentScope? _admission;

    internal GraphRootArgumentScope(RenderGraphContext context, IReadOnlyList<RenderGraphTexture> outputs, bool graphics)
    {
        _context = context;
        _outputs = outputs;
        _graphics = graphics;
    }

    internal IReadOnlyList<ShaderResources>? Resources { get; private set; }

    public void Prepare(ShaderReferenceMetadata references)
    {
        RenderGraphContext context = _context ?? throw new InvalidOperationException("The graph root scope has already been prepared.");
        try
        {
            (Resources, _admission) = context.SelectArguments(references, _outputs!, _graphics);
        }
        finally
        {
            // Native root ownership must not retain the callback context or mutable argument provider.
            _context = null;
            _outputs = null;
        }
    }

    public void Admit(IReadOnlyCollection<IShaderReference> references) => _admission?.Admit(references);
}
