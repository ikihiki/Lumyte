using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Describes logical shader inputs for one render draw or compute dispatch.</summary>
public sealed class RenderGraphShaderResources : ShaderResources
{
    private readonly GraphShaderResourceOwner _owner;

    internal RenderGraphShaderResources(GraphShaderResourceOwner owner, ShaderResources resources)
        : base(resources)
    {
        _owner = owner;
        ValidateBeforeCollection(owner.ValidateBuilding);
    }

    /// <summary>Creates another independent shader operation in this graph.</summary>
    /// <returns>A sibling whose inputs and execution are declared independently.</returns>
    public override RenderGraphShaderResources CreateSibling() => _owner.Create();

    /// <summary>Reads the declared sampled texture through a graph-owned view.</summary>
    /// <param name="texture">The pass-declared sampled image.</param>
    /// <param name="desc">The requested view range.</param>
    /// <returns>A reference available for root or shader-data values after preparation.</returns>
    public ArgumentRegistration<IGraphicsTextureView> ReadTexture(RenderGraphTexture texture, TextureViewDesc? desc = null) => RegisterTexture(() => _owner.Texture(this, texture, desc));

    /// <summary>Reads or writes an exact typed range of a declared buffer.</summary>
    /// <typeparam name="T">The storage element.</typeparam>
    /// <param name="buffer">The declared logical buffer.</param>
    /// <param name="offset">The element base.</param>
    /// <param name="count">The nonzero element count.</param>
    /// <param name="access">The shader's actual resource access.</param>
    /// <returns>A reference available after preparation.</returns>
    public ArgumentRegistration<T> ReadBuffer<T>(RenderGraphBuffer<T> buffer, ulong offset, ulong count, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged => RegisterBuffer(() => _owner.Buffer(this, buffer, access).SliceElements(offset, count), access);

    /// <summary>Reads a declared shader-data range without bypassing copy provenance.</summary>
    /// <typeparam name="T">The shader-data element.</typeparam>
    /// <param name="buffer">The declared logical buffer.</param>
    /// <param name="offset">The element base.</param>
    /// <param name="count">The nonzero element count.</param>
    /// <returns>A reference available after preparation.</returns>
    public ArgumentRegistration<T> ReadBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count)
        where T : struct, IShaderData => RegisterShaderData(() => _owner.Buffer(this, buffer).SliceElements(offset, count));

    /// <summary>Uses an immutable graph-owned sampler configuration.</summary>
    /// <param name="desc">The exact sampler configuration.</param>
    /// <returns>A sampler reference for this operation.</returns>
    public ArgumentRegistration<IGraphicsSampler> Sampler(SamplerDesc desc) => RegisterSampler(_owner.Sampler(desc));
}
