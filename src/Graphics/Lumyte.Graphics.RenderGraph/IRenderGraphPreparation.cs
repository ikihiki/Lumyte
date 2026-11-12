namespace Lumyte.Graphics.RenderGraph;

/// <summary>Prepares owner-local bindings after live resources exist and before any graph commands are recorded.</summary>
public interface IRenderGraphPreparation : IRenderGraphLifetime
{
    /// <summary>Resolves declared live resources synchronously; never submits, waits, or records GPU commands.</summary>
    /// <param name="context">The allocation-only context valid for this preparation call.</param>
    void Prepare(RenderGraphPreparationContext context);
}
