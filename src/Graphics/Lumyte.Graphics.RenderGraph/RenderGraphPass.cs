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

    internal bool IsRoot => HasSideEffects || _uses.Any(use => !use.Resource.IsTransient && RenderGraphValidation.IsWrite(use.Scope));

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

        AddUse(texture, state, scope, discard);
        return this;
    }

    /// <summary>Declares a whole-buffer access with its actual stages and accesses.</summary>
    /// <typeparam name="T">The unmanaged storage element type.</typeparam>
    /// <param name="buffer">A buffer declared in this graph.</param>
    /// <param name="scope">The actual stages and accesses.</param>
    /// <param name="discard">Promises to overwrite every element without preserving prior contents; requires write-only access.</param>
    /// <returns>This pass for further declarations.</returns>
    public RenderGraphPass UseBuffer<T>(RenderGraphBuffer<T> buffer, BarrierScope scope, bool discard = false)
        where T : unmanaged
    {
        AddUse(buffer, TextureState.Undefined, scope, discard);
        return this;
    }

    /// <summary>Declares a shader data access without bypassing transfer metadata.</summary>
    /// <typeparam name="T">The application shader data structure.</typeparam>
    /// <param name="buffer">The imported shader data.</param>
    /// <param name="scope">The actual stages and accesses.</param>
    /// <param name="discard">Promises to overwrite every element without preserving prior contents; requires write-only access.</param>
    /// <returns>This pass.</returns>
    public RenderGraphPass UseShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer, BarrierScope scope, bool discard = false)
        where T : struct, IShaderData
    {
        AddUse(buffer, TextureState.Undefined, scope, discard);
        return this;
    }

    /// <summary>Retains this pass for side effects not expressed as imported resource writes.</summary>
    /// <returns>This pass.</returns>
    public RenderGraphPass KeepAlive()
    {
        HasSideEffects = true;
        return this;
    }

    /// <summary>Adds an ordering dependency for side effects unrelated to resource accesses.</summary>
    /// <param name="pass">A different pass in this graph which must run first.</param>
    /// <returns>This pass for further declarations.</returns>
    public RenderGraphPass DependsOn(RenderGraphPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.Owner != Owner || pass == this)
        {
            throw new ArgumentException("Dependency must be a different pass in the same graph.", nameof(pass));
        }

        _dependencies.Add(pass);
        return this;
    }

    private void AddUse(RenderGraphResource resource, TextureState state, BarrierScope scope, bool discard)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.Owner != Owner)
        {
            throw new ArgumentException("Resource belongs to another graph.", nameof(resource));
        }

        RenderGraphValidation.Scope(scope, required: true);
        resource.Validate(state, scope);
        if (discard && (!RenderGraphValidation.IsWrite(scope) || (scope.Access & ~RenderGraphValidation.Writes) != 0))
        {
            throw new ArgumentException("Discard requires write-only access and overwrites the entire allocation.", nameof(discard));
        }

        if (_uses.Any(use => use.Resource == resource))
        {
            throw new ArgumentException("Declare each resource once per pass, combining its accesses.", nameof(resource));
        }

        _uses.Add(new RenderGraphUse(resource, state, scope, discard));
    }
}
