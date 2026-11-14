using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Resolves only a live pass's declared resources before recording, without exposing a command buffer.</summary>
public sealed class RenderGraphPreparationContext
{
    private readonly CompiledRenderGraph _plan;
    private readonly IReadOnlyList<RenderGraphPass> _passes;
    private readonly IReadOnlyDictionary<RenderGraphPass, RenderGraphUse[]> _uses;
    private bool _active = true;

    internal RenderGraphPreparationContext(CompiledRenderGraph plan, IReadOnlyList<RenderGraphPass> passes, IReadOnlyDictionary<RenderGraphPass, RenderGraphUse[]> uses)
    {
        (_plan, _passes, _uses) = (plan, passes, uses);
    }

    /// <summary>Gets immutable live execution identities in actual recording order.</summary>
    public IReadOnlyList<RenderGraphPass> LivePasses => _passes;

    /// <summary>Resolves a texture declared by this live pass.</summary>
    /// <param name="pass">The exact live execution identity.</param>
    /// <param name="texture">A declared handle.</param>
    /// <returns>The borrowed allocation.</returns>
    public IGraphicsTexture GetTexture(RenderGraphPass pass, RenderGraphTexture texture)
    {
        Check(pass, texture, ResourceAccess.None);
        return (IGraphicsTexture)_plan.Resolve(texture);
    }

    /// <summary>Creates or reuses a plan-owned sampled view after checking this pass's sampled-read declaration.</summary>
    /// <param name="pass">The consuming live pass.</param>
    /// <param name="texture">The declared sampled handle.</param>
    /// <param name="desc">The selected view range.</param>
    /// <returns>A borrowed plan-owned view.</returns>
    public IGraphicsTextureView CreateTextureView(RenderGraphPass pass, RenderGraphTexture texture, TextureViewDesc? desc = null)
    {
        RenderGraphUse use = Check(pass, texture, ResourceAccess.ShaderRead);
        if (use.State != TextureState.Sampled)
        {
            throw new ArgumentException("Sampled argument views require a Sampled declaration.", nameof(texture));
        }

        return _plan.CreateView(texture, desc);
    }

    /// <summary>Resolves a declared raw shader range source.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="pass">The consuming live pass.</param>
    /// <param name="buffer">The declared buffer.</param>
    /// <param name="access">Required shader descriptor accesses.</param>
    /// <returns>The borrowed allocation.</returns>
    public IGraphicsBuffer<T> GetBuffer<T>(RenderGraphPass pass, RenderGraphBuffer<T> buffer, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        if (access is not (ArgumentBufferAccess.Read or ArgumentBufferAccess.Write or ArgumentBufferAccess.ReadWrite))
        {
            throw new ArgumentOutOfRangeException(nameof(access));
        }

        ResourceAccess required = ((access & ArgumentBufferAccess.Read) != 0 ? ResourceAccess.ShaderRead : 0) | ((access & ArgumentBufferAccess.Write) != 0 ? ResourceAccess.ShaderWrite : 0);
        Check(pass, buffer, required);
        return (IGraphicsBuffer<T>)_plan.Resolve(buffer);
    }

    /// <summary>Resolves declared device-memory shader data; upload initialization remains an explicit graph copy.</summary>
    /// <typeparam name="T">The logical shader-data type.</typeparam>
    /// <param name="pass">The consuming live pass.</param>
    /// <param name="buffer">The declared shader data.</param>
    /// <returns>The borrowed allocation.</returns>
    public IGraphicsShaderDataBuffer<T> GetShaderDataBuffer<T>(RenderGraphPass pass, RenderGraphShaderDataBuffer<T> buffer)
        where T : IShaderArguments
    {
        Check(pass, buffer, ResourceAccess.ShaderRead);
        return (IGraphicsShaderDataBuffer<T>)_plan.Resolve(buffer);
    }

    internal IReadOnlyList<RenderGraphUse> GetUses(RenderGraphPass pass)
        => _active && _uses.TryGetValue(pass, out RenderGraphUse[]? uses) ? Array.AsReadOnly(uses) : throw new InvalidOperationException("Pass declarations are unavailable.");

    internal IGraphicsSampler GetSampler(RenderGraphPass pass, RenderGraphSampler sampler)
    {
        Check(pass, sampler, ResourceAccess.ShaderRead);
        return (IGraphicsSampler)_plan.Resolve(sampler);
    }

    internal bool IsGraphAllocation(object allocation) => _plan.ContainsAllocation(allocation);

    internal void CheckAllocation(RenderGraphPass pass, object allocation, ResourceAccess required, bool sampled)
    {
        if (!_active || !_uses.TryGetValue(pass, out RenderGraphUse[]? uses))
        {
            throw new InvalidOperationException("Preparation resolves only live compiled declarations.");
        }

        foreach (RenderGraphUse use in uses)
        {
            if (ReferenceEquals(_plan.Resolve(use.Resource), allocation))
            {
                Check(pass, use.Resource, required);
                if (sampled && use.State != TextureState.Sampled)
                {
                    throw new ArgumentException("A sampled argument requires a Sampled pass declaration.", nameof(allocation));
                }

                return;
            }
        }

        throw new ArgumentException("The argument allocation was not declared by this pass.", nameof(allocation));
    }

    internal void Close() => _active = false;

    private RenderGraphUse Check(RenderGraphPass pass, RenderGraphResource resource, ResourceAccess required)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(resource);
        if (!_active || !_uses.TryGetValue(pass, out RenderGraphUse[]? uses))
        {
            throw new InvalidOperationException("Preparation resolves only live passes during its callback.");
        }

        RenderGraphUse? match = uses.Where(use => ReferenceEquals(use.Resource, resource)).Cast<RenderGraphUse?>().SingleOrDefault();
        if (match is not { } declared || (declared.Scope.Access & required) != required)
        {
            throw new ArgumentException("The shader resource access was not declared by this pass.", nameof(resource));
        }

        return declared;
    }
}
