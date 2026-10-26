using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Resources.Graphics;

/// <summary>Connects a render graph to resources without adding a manager dependency to RenderGraph.</summary>
public static class ResourceRenderGraphExtensions
{
    /// <summary>Registers one shared resource manager for each graph's borrowed import context.</summary>
    /// <param name="extensions">The graph extension builder.</param>
    /// <param name="manager">The borrowed manager whose lifetime covers all graph operations.</param>
    /// <returns>The same builder.</returns>
    public static RenderGraphExtensionsBuilder AddResources(this RenderGraphExtensionsBuilder extensions, ResourceManager manager)
        => extensions.Add(graph => new ResourceRenderGraphImports(graph, manager));

    /// <summary>Gets the resource import context registered for this graph.</summary>
    /// <param name="graph">The parent graph owning imported lifetimes.</param>
    /// <returns>The graph's shared borrowed context.</returns>
    public static ResourceRenderGraphImports ImportResources(this RenderGraphBuilder graph)
        => graph.GetExtension<ResourceRenderGraphImports>();

    /// <summary>Gets or attaches the shared import context, rejecting a different resource manager.</summary>
    /// <param name="graph">The graph receiving imported allocations.</param>
    /// <param name="manager">The shared resource manager.</param>
    /// <returns>A borrowed context whose parent graph owns compilation and disposal.</returns>
    public static ResourceRenderGraphImports ImportResources(this RenderGraphBuilder graph, ResourceManager manager)
    {
        ResourceRenderGraphImports imports = graph.GetOrAddExtension(parent => new ResourceRenderGraphImports(parent, manager));
        if (!ReferenceEquals(imports.Manager, manager))
        {
            throw new ArgumentException("The graph already uses a different resource manager.", nameof(manager));
        }

        return imports;
    }
}
