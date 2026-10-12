namespace Lumyte.Graphics.RenderGraph;

/// <summary>A detached immutable description for diagnostic adapters.</summary>
public sealed class RenderGraphSnapshot
{
    internal RenderGraphSnapshot(string name, long estimatedBytes, RenderGraphPassInfo[] passes, RenderGraphResourceInfo[] resources, RenderGraphDependencyInfo[] dependencies, RenderGraphBarrierInfo[] barriers)
    {
        Name = name;
        EstimatedBytes = estimatedBytes;
        Passes = Array.AsReadOnly(passes);
        Resources = Array.AsReadOnly(resources);
        Dependencies = Array.AsReadOnly(dependencies);
        Barriers = Array.AsReadOnly(barriers);
    }

    /// <summary>Gets the graph name.</summary>
    public string Name { get; }

    /// <summary>Gets the conservative retained metadata estimate used for capture budgeting.</summary>
    /// <remarks>Includes descriptions, arrays and referenced strings; excludes executable plans and native resources.</remarks>
    public long EstimatedBytes { get; }

    /// <summary>Gets all declared passes, including culled passes.</summary>
    public IReadOnlyList<RenderGraphPassInfo> Passes { get; }

    /// <summary>Gets all resource lifetimes in the live execution order.</summary>
    public IReadOnlyList<RenderGraphResourceInfo> Resources { get; }

    /// <summary>Gets ordering dependencies between live passes.</summary>
    public IReadOnlyList<RenderGraphDependencyInfo> Dependencies { get; }

    /// <summary>Gets barriers in recording order, including final exports.</summary>
    public IReadOnlyList<RenderGraphBarrierInfo> Barriers { get; }
}
