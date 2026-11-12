using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Connects logical shader descriptions and native prepared modules to graph execution.</summary>
public static class RenderGraphShaderResourceExtensions
{
    /// <summary>Collects an opaque resource group without choosing an application argument schema.</summary>
    /// <param name="graph">The building graph.</param>
    /// <returns>A logical input description with no native registration handles.</returns>
    public static RenderGraphShaderResources CreateShaderResources(this RenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return GraphShaderResourceOwner.Get(graph).Create();
    }

    /// <summary>Describes the logical resources of an existing indivisible prepared module group.</summary>
    /// <param name="graph">The building graph.</param>
    /// <param name="resources">The graph-owned prepared module group.</param>
    /// <returns>The logical description of that same indivisible group.</returns>
    public static RenderGraphShaderResources DescribeModuleShaderResources(this RenderGraph graph, ShaderResources resources)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var owner = GraphShaderResourceOwner.Get(graph);
        owner.ValidateModule(resources);
        return new(owner, resources);
    }

    /// <summary>Connects a logical operation to its consuming pass and actual color outputs.</summary>
    /// <param name="pass">The consuming pass.</param>
    /// <param name="resources">The logical input description.</param>
    /// <param name="colorAttachments">Actual color outputs, or none for compute or depth-only rendering.</param>
    /// <returns>The pass.</returns>
    public static RenderGraphPass UseShaderResources(this RenderGraphPass pass, RenderGraphShaderResources resources, params RenderGraphTexture[] colorAttachments)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(resources);
        GraphShaderResourceOwner.Get(pass.Owner).Use(pass, resources.Core, colorAttachments);
        return pass;
    }

    /// <summary>Creates native resource storage for an independently prepared module, not logical graph shader inputs.</summary>
    /// <param name="graph">The building graph.</param>
    /// <returns>An opaque module resource set with no table or reservation accessor.</returns>
    public static ShaderResources CreateModuleShaderResources(this RenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return GraphShaderResourceOwner.Get(graph).CreateModule();
    }

    /// <summary>Resolves a prepared module's sampled logical image without returning a pending registration.</summary>
    /// <param name="graph">The building graph.</param>
    /// <param name="resources">The graph-owned module group.</param>
    /// <param name="texture">The declared sampled image.</param>
    /// <param name="resolved">Receives the real reference during global preparation.</param>
    /// <param name="desc">The requested view range.</param>
    public static void RegisterModuleTexture(this RenderGraph graph, ShaderResources resources, RenderGraphTexture texture, Action<IGpuRef<IGraphicsTextureView>> resolved, TextureViewDesc? desc = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var owner = GraphShaderResourceOwner.Get(graph);
        owner.ValidateModule(resources);
        resources.RegisterTexture(() => owner.Texture(resources, texture, desc), resolved);
    }

    /// <summary>Resolves a native module's typed logical buffer during preparation.</summary>
    /// <typeparam name="T">The unmanaged storage element.</typeparam>
    /// <param name="graph">The building graph.</param>
    /// <param name="resources">The graph-owned module group.</param>
    /// <param name="buffer">The pass-declared buffer.</param>
    /// <param name="offset">The element base.</param>
    /// <param name="count">The element count.</param>
    /// <param name="resolved">Receives the real native reference.</param>
    /// <param name="access">The actual shader access.</param>
    public static void RegisterModuleBuffer<T>(this RenderGraph graph, ShaderResources resources, RenderGraphBuffer<T> buffer, ulong offset, ulong count, Action<IGpuRef<T>> resolved, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(graph);
        var owner = GraphShaderResourceOwner.Get(graph);
        owner.ValidateModule(resources);
        resources.RegisterBuffer(() => owner.Buffer(resources, buffer, access).SliceElements(offset, count), resolved, access);
    }

    /// <summary>Resolves a native module's logical shader-data buffer during preparation.</summary>
    /// <typeparam name="T">The shader-data element.</typeparam>
    /// <param name="graph">The building graph.</param>
    /// <param name="resources">The graph-owned module group.</param>
    /// <param name="buffer">The declared shader-data buffer.</param>
    /// <param name="offset">The element base.</param>
    /// <param name="count">The element count.</param>
    /// <param name="resolved">Receives the real native reference.</param>
    public static void RegisterModuleBuffer<T>(this RenderGraph graph, ShaderResources resources, RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count, Action<IGpuRef<T>> resolved)
        where T : struct, IShaderData
    {
        ArgumentNullException.ThrowIfNull(graph);
        var owner = GraphShaderResourceOwner.Get(graph);
        owner.ValidateModule(resources);
        resources.RegisterShaderData(() => owner.Buffer(resources, buffer).SliceElements(offset, count), resolved);
    }

    /// <summary>Connects an independently prepared native module to its consuming pass.</summary>
    /// <param name="pass">The consuming pass.</param>
    /// <param name="resources">The module's native resource group.</param>
    /// <param name="colorAttachments">Actual color outputs, or none for compute.</param>
    /// <returns>The pass.</returns>
    public static RenderGraphPass UseShaderResources(this RenderGraphPass pass, ShaderResources resources, params RenderGraphTexture[] colorAttachments)
    {
        ArgumentNullException.ThrowIfNull(pass);
        GraphShaderResourceOwner.Get(pass.Owner).Use(pass, resources, colorAttachments);
        return pass;
    }

    /// <summary>Gets an immutable sampler owned by the graph.</summary>
    /// <param name="graph">The building graph.</param>
    /// <param name="desc">The sampler configuration.</param>
    /// <returns>A borrowed sampler.</returns>
    public static IGraphicsSampler GetSampler(this RenderGraph graph, SamplerDesc desc)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return GraphShaderResourceOwner.Get(graph).Sampler(desc);
    }
}
