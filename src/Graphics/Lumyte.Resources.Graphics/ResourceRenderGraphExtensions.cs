using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Resources.Graphics;

/// <summary>Connects a render graph to resources without adding a manager dependency to RenderGraph.</summary>
public static class ResourceRenderGraphExtensions
{
    /// <summary>Starts an import scope; compile through this scope to transfer leases to the plan.</summary>
    /// <param name="graph">The graph receiving imported allocations.</param>
    /// <param name="manager">The shared resource manager.</param>
    /// <returns>A scope disposed on abandonment, or transferred to its compiled plan.</returns>
    public static ResourceRenderGraphImports ImportResources(this RenderGraphBuilder graph, ResourceManager manager) => new(graph, manager);
}
