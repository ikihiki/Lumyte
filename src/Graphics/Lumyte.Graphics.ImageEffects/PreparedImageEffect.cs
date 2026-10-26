using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Owns fixed resources captured by appended effect passes.</summary>
public sealed class PreparedImageEffect : IRenderGraphLifetime
{
    private readonly EffectPreparationContext _context;

    internal PreparedImageEffect(EffectImage output, EffectPreparationContext context)
    {
        Output = output;
        _context = context;
    }

    /// <summary>Gets the graph-local result.</summary>
    public EffectImage Output { get; }

    /// <inheritdoc/>
    void IRenderGraphLifetime.Validate() => _context.EnsureAlive();

    /// <summary>Releases fixed resources after GPU completion and graph temporary-resource disposal.</summary>
    public void Dispose() => _context.Release();
}
