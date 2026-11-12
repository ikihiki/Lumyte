using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Connects logical shader inputs and their render or compute execution to graph passes.</summary>
public static class RenderGraphShaderResourceExtensions
{
    /// <summary>Starts the logical inputs for one indivisible shader operation.</summary>
    /// <param name="graph">The building graph.</param>
    /// <returns>Graph-owned inputs; connect them to exactly one consuming pass.</returns>
    public static RenderGraphShaderResources CreateShaderResources(this RenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return GraphShaderResourceOwner.Get(graph).Create();
    }

    /// <summary>Declares one shader operation and its actual color outputs; no outputs means compute.</summary>
    /// <param name="pass">The consuming pass.</param>
    /// <param name="resources">The operation's logical inputs.</param>
    /// <param name="colorAttachments">The actual render outputs, already declared ColorAttachment/ColorWrite, or none for compute.</param>
    /// <returns>The pass for further declarations.</returns>
    public static RenderGraphPass UseShaderResources(this RenderGraphPass pass, ShaderResources resources, params RenderGraphTexture[] colorAttachments)
    {
        ArgumentNullException.ThrowIfNull(pass);
        GraphShaderResourceOwner.Get(pass.Owner).Use(pass, resources, colorAttachments);
        return pass;
    }

    /// <summary>Gets an immutable sampler owned by this graph's normal lifetime.</summary>
    /// <param name="graph">The building graph.</param>
    /// <param name="desc">The sampler configuration.</param>
    /// <returns>A borrowed sampler, never disposed by the caller.</returns>
    public static IGraphicsSampler GetSampler(this RenderGraph graph, SamplerDesc desc)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return GraphShaderResourceOwner.Get(graph).Sampler(desc);
    }
}
