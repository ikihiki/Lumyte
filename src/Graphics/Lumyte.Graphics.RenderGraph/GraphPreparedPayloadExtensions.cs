using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

// Assembly-private payload linkage. Eligibility and capacity still come from standard pass Use declarations.
internal static class GraphPreparedPayloadExtensions
{
    internal static ShaderResources CreatePreparedPayload(this RenderGraph graph) => GraphShaderResourceOwner.Get(graph).CreateModule();

    internal static RenderGraphPass AttachPreparedPayload(this RenderGraphPass pass, ShaderResources resources)
    {
        GraphShaderResourceOwner.Get(pass.Owner).Attach(pass, resources);
        return pass;
    }

    internal static void ConnectPreparedTexture(this RenderGraph graph, ShaderResources resources, RenderGraphTexture texture, Action<IGpuRef<IGraphicsTextureView>> resolved, TextureViewDesc? desc = null)
    {
        var owner = GraphShaderResourceOwner.Get(graph);
        owner.ValidateModule(resources);
        resources.RegisterTexture(() => owner.Texture(resources, texture, desc), resolved);
    }

    internal static void ConnectPreparedBuffer<T>(this RenderGraph graph, ShaderResources resources, RenderGraphBuffer<T> buffer, ulong offset, ulong count, Action<IGpuRef<T>> resolved, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        var owner = GraphShaderResourceOwner.Get(graph);
        owner.ValidateModule(resources);
        resources.RegisterBuffer(() => owner.Buffer(resources, buffer, access).SliceElements(offset, count), resolved, access);
    }

    internal static void ConnectPreparedBuffer<T>(this RenderGraph graph, ShaderResources resources, RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count, Action<IGpuRef<T>> resolved)
        where T : IShaderData
    {
        var owner = GraphShaderResourceOwner.Get(graph);
        owner.ValidateModule(resources);
        resources.RegisterShaderData(() => owner.Buffer(resources, buffer).SliceElements(offset, count), resolved);
    }

    internal static IGraphicsSampler GetPreparedSampler(this RenderGraph graph, SamplerDesc desc) => GraphShaderResourceOwner.Get(graph).Sampler(desc);
}
