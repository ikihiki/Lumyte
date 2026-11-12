using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Declares the resources and explicit dependencies of one recording callback.</summary>
public sealed class RenderGraphPass
{
    private readonly List<RenderGraphUse> _uses = [];
    private readonly HashSet<RenderGraphPass> _dependencies = [];

    internal RenderGraphPass(RenderGraph owner, string name, Action<RenderGraphContext> record)
    {
        Owner = owner;
        Name = name;
        Record = record;
    }

    /// <summary>Gets the diagnostic pass name.</summary>
    public string Name { get; }

    internal bool IsRoot => HasSideEffects || _uses.Any(use => !use.Resource.IsTransient && use.Resource.LocalPassOwner is null && RenderGraphValidation.IsWrite(use.Scope));

    internal bool HasSideEffects { get; private set; }

    internal RenderGraph Owner { get; }

    internal Action<RenderGraphContext> Record { get; }

    internal IReadOnlyList<RenderGraphUse> Uses => _uses;

    internal IReadOnlySet<RenderGraphPass> Dependencies => _dependencies;

    /// <summary>Declares a whole-texture access; use a combined scope for read/write attachments.</summary>
    /// <param name="texture">A texture declared in this graph.</param>
    /// <param name="state">The required state during the callback.</param>
    /// <param name="scope">The actual stages and accesses.</param>
    /// <param name="discard">Promises to overwrite every mip and layer without preserving prior contents; requires write-only access.</param>
    /// <returns>This pass for further declarations.</returns>
    public RenderGraphPass UseTexture(RenderGraphTexture texture, TextureState state, BarrierScope scope, bool discard = false)
    {
        if (state is TextureState.Undefined or TextureState.Present)
        {
            throw new ArgumentException("A pass must declare a defined texture state.", nameof(state));
        }

        GraphShaderBinding? binding = state == TextureState.Sampled && (scope.Access & ResourceAccess.ShaderRead) != 0
            ? new(texture, 0, 0, false, ArgumentBufferAccess.Read, lookup => lookup.ReadTexture(texture)) : null;
        AddUse(texture, state, scope, discard, binding);
        return this;
    }

    /// <summary>Declares a whole-buffer access with its actual stages and accesses.</summary>
    /// <typeparam name="T">The unmanaged storage element type.</typeparam>
    /// <param name="buffer">A buffer declared in this graph.</param>
    /// <param name="scope">The actual stages and accesses.</param>
    /// <param name="discard">Promises to overwrite every element without preserving prior contents; requires write-only access.</param>
    /// <returns>This pass for further declarations.</returns>
    public RenderGraphPass UseBuffer<T>(RenderGraphBuffer<T> buffer, BarrierScope scope, bool discard = false)
        where T : unmanaged => UseBufferCore(buffer, 0, buffer.Count, scope, discard, repeated: false);

    /// <summary>Declares a shader data access without bypassing transfer metadata.</summary>
    /// <typeparam name="T">The application shader data structure.</typeparam>
    /// <param name="buffer">The imported shader data.</param>
    /// <param name="scope">The actual stages and accesses.</param>
    /// <param name="discard">Promises to overwrite every element without preserving prior contents; requires write-only access.</param>
    /// <returns>This pass.</returns>
    public RenderGraphPass UseShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, BarrierScope scope, bool discard = false)
        where T : struct, IShaderData => UseShaderDataCore(buffer, 0, buffer.Count, scope, discard, repeated: false);

    /// <summary>Declares borrowed storage whose writes are confined to this pass, without making an otherwise dead pass observable.</summary>
    /// <typeparam name="T">The unmanaged storage element.</typeparam>
    /// <param name="buffer">The pass-owned allocation, retained by its owner through completion.</param>
    /// <param name="scope">All actual intra-pass accesses, including copies and shader reads.</param>
    /// <returns>This pass; an existing global import retains its global observability.</returns>
    public RenderGraphPass UseLocalBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope scope)
        where T : unmanaged => UseBufferCore(Owner.LocalBuffer(this, buffer, scope), 0, buffer.Count, scope, discard: false, repeated: false);

    /// <summary>Declares pass-confined borrowed shader data while preserving transfer metadata and validation.</summary>
    /// <typeparam name="T">The shader-data element.</typeparam>
    /// <param name="buffer">The pass-owned allocation.</param>
    /// <param name="scope">All actual intra-pass accesses.</param>
    /// <returns>This pass; the caller still owns the allocation.</returns>
    public RenderGraphPass UseLocalShaderDataBuffer<T>(IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope)
        where T : struct, IShaderData => UseShaderDataCore(Owner.LocalShaderData(this, buffer, scope), 0, buffer.Count, scope, discard: false, repeated: false);

    /// <summary>Declares an exact sampled view while hazards remain conservatively allocation-wide.</summary>
    /// <param name="texture">The logical image.</param>
    /// <param name="view">The exact sampled view admitted before recording.</param>
    /// <param name="state">Must be Sampled.</param>
    /// <param name="scope">The actual shader-read stages.</param>
    /// <returns>This pass.</returns>
    public RenderGraphPass UseTexture(RenderGraphTexture texture, TextureViewDesc view, TextureState state, BarrierScope scope)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (state != TextureState.Sampled || (scope.Access & ResourceAccess.ShaderRead) == 0)
        {
            throw new ArgumentException("Exact argument views require sampled shader-read access.");
        }

        AddUse(texture, state, scope, false, new(texture, 0, 0, false, ArgumentBufferAccess.Read, lookup => lookup.ReadTexture(texture, view)), repeated: true);
        return this;
    }

    /// <summary>Declares an exact element range for shader binding; hazards remain allocation-wide.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="buffer">The logical buffer.</param>
    /// <param name="offset">The element offset.</param>
    /// <param name="count">The positive element count.</param>
    /// <param name="scope">The actual accesses, the sole source of shader read/write permissions.</param>
    /// <returns>This pass.</returns>
    public RenderGraphPass UseBuffer<T>(RenderGraphBuffer<T> buffer, ulong offset, ulong count, BarrierScope scope)
        where T : unmanaged => UseBufferCore(buffer, offset, count, scope, discard: false, repeated: true);

    /// <summary>Declares an exact shader-data range, including copy metadata coherence.</summary>
    /// <typeparam name="T">The shader-data element type.</typeparam>
    /// <param name="buffer">The logical shader-data buffer.</param>
    /// <param name="offset">The element offset.</param>
    /// <param name="count">The positive element count.</param>
    /// <param name="scope">The actual accesses.</param>
    /// <returns>This pass.</returns>
    public RenderGraphPass UseShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count, BarrierScope scope)
        where T : struct, IShaderData => UseShaderDataCore(buffer, offset, count, scope, discard: false, repeated: true);

    /// <summary>Declares a borrowed immutable sampler by identity without introducing a barrier or write root.</summary>
    /// <param name="sampler">The sampler to retain through GPU completion.</param>
    /// <returns>This pass.</returns>
    public RenderGraphPass UseSampler(IGraphicsSampler sampler) => UseSamplerCore(Owner.GetSamplerResource(sampler));

    /// <summary>Declares a graph-owned immutable sampler by value.</summary>
    /// <param name="desc">The sampler description resolved by the callback.</param>
    /// <returns>This pass.</returns>
    public RenderGraphPass UseSampler(SamplerDesc desc) => UseSamplerCore(Owner.GetSamplerResource(desc));

    /// <summary>Retains this pass for side effects not expressed as imported resource writes.</summary>
    /// <returns>This pass.</returns>
    public RenderGraphPass KeepAlive()
    {
        Owner.EnsureBuilding();
        HasSideEffects = true;
        return this;
    }

    /// <summary>Adds an ordering dependency for side effects unrelated to resource accesses.</summary>
    /// <param name="pass">A different pass in this graph which must run first.</param>
    /// <returns>This pass for further declarations.</returns>
    public RenderGraphPass DependsOn(RenderGraphPass pass)
    {
        Owner.EnsureBuilding();
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.Owner != Owner || pass == this)
        {
            throw new ArgumentException("Dependency must be a different pass in the same graph.", nameof(pass));
        }

        _dependencies.Add(pass);
        return this;
    }

    internal RenderGraphPass UseNativeView(RenderGraphTexture texture, IGraphicsTextureView view, BarrierScope scope)
    {
        AddUse(texture, TextureState.Sampled, scope, false, new(texture, 0, 0, false, ArgumentBufferAccess.Read, lookup => lookup.ReadNativeTexture(texture, view)), repeated: true);
        return this;
    }

    internal RenderGraphPass UseLocalBuffer<T>(BufferSlice<T> range, BarrierScope scope)
        where T : unmanaged => UseBufferCore(Owner.LocalBuffer(this, range.Buffer, scope), range.Offset, range.Count, scope, discard: false, repeated: true);

    internal RenderGraphPass UseLocalShaderDataBuffer<T>(ShaderDataSlice<T> range, BarrierScope scope)
        where T : struct, IShaderData => UseShaderDataCore(Owner.LocalShaderData(this, range.Buffer, scope), range.Offset, range.Count, scope, discard: false, repeated: true);

    private static ArgumentBufferAccess? ShaderAccess(BarrierScope scope)
    {
        ResourceAccess access = scope.Access & (ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite);
        return access == ResourceAccess.None ? null : access == ResourceAccess.ShaderRead ? ArgumentBufferAccess.Read : access == ResourceAccess.ShaderWrite ? ArgumentBufferAccess.Write : ArgumentBufferAccess.ReadWrite;
    }

    private static void Range(ulong offset, ulong count, ulong total)
    {
        if (count == 0 || offset > total || count > total - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
    }

    private RenderGraphPass UseBufferCore<T>(RenderGraphBuffer<T> buffer, ulong offset, ulong count, BarrierScope scope, bool discard, bool repeated)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Range(offset, count, buffer.Count);
        ArgumentBufferAccess? access = ShaderAccess(scope);
        GraphShaderBinding binding = new(buffer, offset, count, false, access, access is { } actual ? lookup => lookup.ReadBuffer(buffer, offset, count, actual) : null);
        AddUse(buffer, TextureState.Undefined, scope, discard, binding, repeated);
        return this;
    }

    private RenderGraphPass UseShaderDataCore<T>(RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count, BarrierScope scope, bool discard, bool repeated)
        where T : struct, IShaderData
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Range(offset, count, buffer.Count);
        ArgumentBufferAccess? access = ShaderAccess(scope);
        GraphShaderBinding binding = new(buffer, offset, count, true, access, access is not null ? lookup => lookup.ReadBuffer(buffer, offset, count) : null);
        AddUse(buffer, TextureState.Undefined, scope, discard, binding, repeated);
        return this;
    }

    private RenderGraphPass UseSamplerCore(RenderGraphSampler sampler)
    {
        BarrierScope scope = new(PipelineStage.VertexShader | PipelineStage.FragmentShader | PipelineStage.ComputeShader, ResourceAccess.ShaderRead);
        AddUse(sampler, TextureState.Undefined, scope, false, new(sampler, 0, 1, false, ArgumentBufferAccess.Read, lookup => lookup.Sampler(sampler)), repeated: true);
        return this;
    }

    private void AddUse(RenderGraphResource resource, TextureState state, BarrierScope scope, bool discard, GraphShaderBinding? binding = null, bool repeated = false)
    {
        Owner.EnsureBuilding();
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.Owner != Owner)
        {
            throw new ArgumentException("Resource belongs to another graph.", nameof(resource));
        }

        if (resource.LocalPassOwner is not null && !ReferenceEquals(resource.LocalPassOwner, this))
        {
            throw new ArgumentException("Pass-local storage cannot be accessed by another pass.", nameof(resource));
        }

        RenderGraphValidation.Scope(scope, required: true);
        resource.Validate(state, scope);
        if (discard && (!RenderGraphValidation.IsWrite(scope) || (scope.Access & ~RenderGraphValidation.Writes) != 0))
        {
            throw new ArgumentException("Discard requires write-only access and overwrites the entire allocation.", nameof(discard));
        }

        int previous = _uses.FindIndex(use => ReferenceEquals(use.Resource, resource));
        if (previous >= 0)
        {
            RenderGraphUse existing = _uses[previous];
            if (!repeated || existing.State != state)
            {
                throw new ArgumentException("Combine accesses for one resource; distinct explicit binding views/ranges may share an allocation.", nameof(resource));
            }

            if (binding is { Access: not null } && (existing.Bindings ?? []).Any(value => value.Access is not null && value.Offset == binding.Offset && value.Count == binding.Count && value.Access != binding.Access))
            {
                throw new ArgumentException("Combine shader accesses for an identical binding range.");
            }

            GraphShaderBinding[] bindings = binding is null ? (existing.Bindings ?? []).ToArray() : (existing.Bindings ?? []).Append(binding).ToArray();
            _uses[previous] = existing with { Scope = new(existing.Scope.Stages | scope.Stages, existing.Scope.Access | scope.Access), Discard = existing.Discard && discard, Bindings = bindings };
        }
        else
        {
            _uses.Add(new RenderGraphUse(resource, state, scope, discard, binding is null ? null : [binding]));
        }
    }
}
