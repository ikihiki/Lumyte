namespace Lumyte.Graphics.ImageEffects;

/// <summary>Owns fixed resources captured by appended effect passes.</summary>
public sealed class PreparedImageEffect : IDisposable
{
    private readonly EffectPreparationContext _context;

    internal PreparedImageEffect(EffectImage output, EffectPreparationContext context)
    {
        Output = output;
        _context = context;
    }

    /// <summary>Gets the graph-local result.</summary>
    public EffectImage Output { get; }

    /// <summary>Releases fixed resources after GPU completion and graph-plan disposal.</summary>
    public void Dispose() => _context.Release();
}
