using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Collects the logical resources for one indivisible graph shader operation.</summary>
public sealed class RenderGraphShaderResources
{
    private readonly GraphShaderResourceOwner _owner;
    private readonly Dictionary<ResourceKey, object?> _references = new(new ResourceKeyComparer());

    internal RenderGraphShaderResources(GraphShaderResourceOwner owner, ShaderResources resources)
    {
        _owner = owner;
        Core = resources;
    }

    internal ShaderResources Core { get; }

    /// <summary>Declares a sampled logical image and view before graph compilation.</summary>
    /// <param name="texture">The logical image.</param>
    /// <param name="desc">The exact view description, or its default.</param>
    /// <returns>This resource group.</returns>
    public RenderGraphShaderResources ReadTexture(RenderGraphTexture texture, TextureViewDesc? desc = null)
    {
        var key = new ResourceKey(texture, desc ?? new TextureViewDesc(), 0, 0, ArgumentBufferAccess.Read);
        if (Add(key))
        {
            Core.RegisterTexture(() => _owner.Texture(Core, texture, desc), reference => Publish(key, reference));
        }

        return this;
    }

    /// <summary>Declares a graph-owned immutable sampler.</summary>
    /// <param name="desc">The sampler description used again for lookup.</param>
    /// <returns>This resource group.</returns>
    public RenderGraphShaderResources Sampler(SamplerDesc desc)
    {
        var key = new ResourceKey(desc, null, 0, 0, ArgumentBufferAccess.Read);
        if (Add(key))
        {
            Core.RegisterSampler(_owner.Sampler(desc), reference => Publish(key, reference));
        }

        return this;
    }

    /// <summary>Declares a borrowed immutable sampler by object identity.</summary>
    /// <param name="sampler">The borrowed sampler.</param>
    /// <returns>This resource group.</returns>
    public RenderGraphShaderResources Sampler(IGraphicsSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        var key = new ResourceKey(sampler, null, 0, 0, ArgumentBufferAccess.Read);
        if (Add(key))
        {
            Core.RegisterSampler(sampler, reference => Publish(key, reference));
        }

        return this;
    }

    /// <summary>Declares an exact typed logical buffer range and its shader access.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="buffer">The logical buffer.</param>
    /// <param name="offset">The element offset.</param>
    /// <param name="count">The positive element count.</param>
    /// <param name="access">The actual shader access.</param>
    /// <returns>This resource group.</returns>
    public RenderGraphShaderResources ReadBuffer<T>(RenderGraphBuffer<T> buffer, ulong offset, ulong count, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfZero(count);
        var key = new ResourceKey(buffer, null, offset, count, access);
        if (Add(key))
        {
            Core.RegisterBuffer(() => _owner.Buffer(Core, buffer, access).SliceElements(offset, count), reference => Publish(key, reference), access);
        }

        return this;
    }

    /// <summary>Declares an exact logical shader-data range including its transitive resources.</summary>
    /// <typeparam name="T">The shader-data element type.</typeparam>
    /// <param name="buffer">The logical shader-data buffer.</param>
    /// <param name="offset">The element offset.</param>
    /// <param name="count">The positive element count.</param>
    /// <returns>This resource group.</returns>
    public RenderGraphShaderResources ReadBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count)
        where T : struct, IShaderData
    {
        ArgumentOutOfRangeException.ThrowIfZero(count);
        var key = new ResourceKey(buffer, null, offset, count, ArgumentBufferAccess.Read);
        if (Add(key))
        {
            Core.RegisterShaderData(() => _owner.Buffer(Core, buffer).SliceElements(offset, count), reference => Publish(key, reference));
        }

        return this;
    }

    internal IGpuRef<IGraphicsTextureView> Resolve(RenderGraphContext context, RenderGraphTexture texture, TextureViewDesc? desc)
        => Resolve<IGraphicsTextureView>(context, new(texture, desc ?? new TextureViewDesc(), 0, 0, ArgumentBufferAccess.Read));

    internal IGpuRef<IGraphicsSampler> Resolve(RenderGraphContext context, SamplerDesc desc)
        => Resolve<IGraphicsSampler>(context, new(desc, null, 0, 0, ArgumentBufferAccess.Read));

    internal IGpuRef<IGraphicsSampler> Resolve(RenderGraphContext context, IGraphicsSampler sampler)
        => Resolve<IGraphicsSampler>(context, new(sampler, null, 0, 0, ArgumentBufferAccess.Read));

    internal IGpuRef<T> Resolve<T>(RenderGraphContext context, RenderGraphBuffer<T> buffer, ulong offset, ulong count, ArgumentBufferAccess access)
        where T : unmanaged => Resolve<T>(context, new(buffer, null, offset, count, access));

    internal IGpuRef<T> Resolve<T>(RenderGraphContext context, RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count)
        where T : struct, IShaderData => Resolve<T>(context, new(buffer, null, offset, count, ArgumentBufferAccess.Read));

    private bool Add(ResourceKey key)
    {
        _owner.ValidateBuilding();
        ArgumentNullException.ThrowIfNull(key.Resource);
        if (key.Resource is RenderGraphResource resource)
        {
            _owner.ValidateResource(resource);
        }

        return _references.TryAdd(key, null);
    }

    private void Publish<T>(ResourceKey key, IGpuRef<T> reference) => _references[key] = reference;

    private IGpuRef<T> Resolve<T>(RenderGraphContext context, ResourceKey key)
    {
        context.EnsureActive();
        _owner.ValidateLookup(Core, context.Pass);
        if (!_references.TryGetValue(key, out object? reference) || reference is not IGpuRef<T> typed)
        {
            throw new ArgumentException("Declare this exact logical resource, view and range before compilation.");
        }

        return typed;
    }

    private sealed class ResourceKeyComparer : IEqualityComparer<ResourceKey>
    {
        public bool Equals(ResourceKey? x, ResourceKey? y)
            => ReferenceEquals(x, y) || (x is not null && y is not null &&
                (x.Resource is SamplerDesc ? x.Resource.Equals(y.Resource) : ReferenceEquals(x.Resource, y.Resource)) &&
                Equals(x.View, y.View) && x.Offset == y.Offset && x.Count == y.Count && x.Access == y.Access);

        public int GetHashCode(ResourceKey key)
            => HashCode.Combine(key.Resource is SamplerDesc ? key.Resource.GetHashCode() : RuntimeHelpers.GetHashCode(key.Resource), key.View, key.Offset, key.Count, key.Access);
    }

    private sealed record ResourceKey(object Resource, TextureViewDesc? View, ulong Offset, ulong Count, ArgumentBufferAccess Access);
}
