namespace Lumyte.Resources.Graphics.Drawing2D;

/// <summary>Retains the coherent CPU shader inputs used to rebuild a 2D renderer's device resources.</summary>
/// <param name="program">The vertex/fragment artifact pair.</param>
public sealed class Renderer2DArtifacts(GraphicsProgramArtifacts program)
{
    /// <summary>Gets the retained shader pair.</summary>
    public GraphicsProgramArtifacts Program { get; } = program ?? throw new ArgumentNullException(nameof(program));
}
