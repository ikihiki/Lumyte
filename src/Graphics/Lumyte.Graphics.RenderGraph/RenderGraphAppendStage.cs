namespace Lumyte.Graphics.RenderGraph;

// Bounded per-call failure injection; never retained by the graph.
internal enum RenderGraphAppendStage
{
    Validated,
    PassAdded,
    LifetimeAdded,
}
