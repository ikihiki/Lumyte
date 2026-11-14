using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Collects the logical resources for one indivisible graph shader operation.</summary>
internal sealed class GraphBindingLookup
{
    private readonly GraphShaderResourceOwner _owner;
    private readonly Dictionary<ResourceKey, object?> _references = new(new ResourceKeyComparer());

    internal GraphBindingLookup(GraphShaderResourceOwner owner, ShaderResources resources)
    {
        _owner = owner;
        Core = resources;
    }

    internal ShaderResources Core { get; }

    /// <summary>Declares a sampled logical image and view before graph compilation.</summary>
    /// <param name="texture">The logical image.</param>
    /// <param name="desc">The exact view description, or its default.</param>
    /// <returns>This resource group.</returns>
    internal GraphBindingLookup ReadTexture(RenderGraphTexture texture, TextureViewDesc? desc = null)
    {
        var key = new ResourceKey(texture, desc ?? new TextureViewDesc(), 0, 0, ArgumentBufferAccess.Read);
        if (Add(key))
        {
            Core.RegisterTexture(() => _owner.Texture(Core, texture, desc), reference => Publish(key, reference));
        }

        return this;
    }

    internal GraphBindingLookup Sampler(RenderGraphSampler sampler)
    {
        var key = new ResourceKey(sampler, null, 0, 0, ArgumentBufferAccess.Read);
        if (Add(key))
        {
            Core.RegisterSampler(_owner.Sampler(Core, sampler), reference => Publish(key, reference));
        }

        return this;
    }

    internal GraphBindingLookup ReadNativeTexture(RenderGraphTexture texture, IGraphicsTextureView view)
    {
        var key = new ResourceKey(view, null, 0, 0, ArgumentBufferAccess.Read);
        if (Add(key))
        {
            Core.RegisterTexture(() => _owner.BorrowedView(Core, texture, view), reference => Publish(key, reference));
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
    internal GraphBindingLookup ReadBuffer<T>(RenderGraphBuffer<T> buffer, ulong offset, ulong count, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
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
    internal GraphBindingLookup ReadBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count)
        where T : struct, IShaderArguments
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

    internal IGpuRef<IGraphicsSampler> ResolveSampler(RenderGraphContext context, object key)
    {
        context.EnsureActive();
        _owner.ValidateLookup(Core, context.Pass);
        foreach ((ResourceKey declared, object? reference) in _references)
        {
            if (declared.Resource is RenderGraphSampler sampler &&
                (sampler.Key is SamplerDesc desc ? desc.Equals(key) : ReferenceEquals(sampler.Key, key)))
            {
                return (IGpuRef<IGraphicsSampler>)reference!;
            }
        }

        throw new ArgumentException("Declare this sampler on the pass before recording.");
    }

    internal IGpuRef<T> ResolveBuffer<T>(RenderGraphContext context, RenderGraphResource buffer, ulong offset, ulong count)
    {
        context.EnsureActive();
        _owner.ValidateLookup(Core, context.Pass);
        foreach ((ResourceKey key, object? reference) in _references)
        {
            if (ReferenceEquals(key.Resource, buffer) && key.Offset == offset && key.Count == count && reference is IGpuRef<T> typed)
            {
                return typed;
            }
        }

        throw new ArgumentException("Declare this exact typed buffer range and shader access on the pass.");
    }

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
