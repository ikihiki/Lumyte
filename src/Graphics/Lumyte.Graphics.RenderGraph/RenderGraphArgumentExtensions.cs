namespace Lumyte.Graphics.RenderGraph;

/// <summary>Provides one graph-owned argument allocator without global caches or submission ownership.</summary>
public static class RenderGraphArgumentExtensions
{
    /// <summary>Gets the allocator owned by this graph and transferred to its compiled plan.</summary>
    /// <param name="graph">The building graph.</param>
    /// <returns>The borrowed graph-local owner.</returns>
    public static RenderGraphArguments GetArguments(this RenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.GetOrAddExtension(owner =>
        {
            var arguments = new RenderGraphArguments(owner);
            owner.Retain(arguments);
            return arguments;
        });
    }
}
