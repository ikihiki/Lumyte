using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Holds a one-shot, immutable plan; imported allocations remain caller-owned.</summary>
public sealed class CompiledRenderGraph
{
    private readonly RenderGraph _owner;
    private readonly Dictionary<RenderGraphResource, (TextureState State, BarrierScope Scope)> _states = [];
    private readonly List<CompiledPass> _passes = [];
    private bool _recorded;

    internal CompiledRenderGraph(RenderGraph owner, IReadOnlyList<RenderGraphPass> passes)
    {
        _owner = owner;
        for (int passIndex = 0; passIndex < passes.Count; passIndex++)
        {
            RenderGraphPass pass = passes[passIndex];
            var barriers = new List<Action<IGraphicsCommandBuffer>>();
            RenderGraphUse[] uses = pass.Uses.ToArray();
            foreach (RenderGraphUse use in uses)
            {
                (TextureState state, BarrierScope scope) = _states.GetValueOrDefault(use.Resource, (use.Resource.InitialState, use.Resource.InitialScope));
                if (use.Resource is RenderGraphTexture && state == TextureState.Undefined &&
                    (use.Scope.Access & ~RenderGraphValidation.Writes) != 0)
                {
                    throw new InvalidOperationException($"Pass '{pass.Name}' reads an undefined texture. Initialize it in an earlier pass.");
                }

                bool transition = state != use.State;
                bool hazard = scope.Access != ResourceAccess.None && (RenderGraphValidation.IsWrite(scope) || RenderGraphValidation.IsWrite(use.Scope));
                if (transition || hazard)
                {
                    BarrierScope after = FollowingReaderScope(passes, passIndex, use);
                    barriers.Add(commands => use.Resource.Barrier(commands, state, use.State, scope, after));
                    _states[use.Resource] = (use.State, after);
                }
                else
                {
                    _states[use.Resource] = (use.State, new BarrierScope(scope.Stages | use.Scope.Stages, scope.Access | use.Scope.Access));
                }
            }

            _passes.Add(new CompiledPass(pass.Record, uses, barriers));
        }

        PassNames = Array.AsReadOnly(passes.Select(pass => pass.Name).ToArray());
    }

    /// <summary>Gets pass names in execution order.</summary>
    public IReadOnlyList<string> PassNames { get; }

    /// <summary>Gets the planned final texture state, or the initial state if unused.</summary>
    /// <param name="texture">A texture imported into the original graph.</param>
    /// <returns>The state after successful execution, without querying the GPU.</returns>
    public TextureState GetFinalTextureState(RenderGraphTexture texture)
    {
        CheckResource(texture);
        return _states.GetValueOrDefault(texture, (texture.InitialState, texture.InitialScope)).Item1;
    }

    /// <summary>Gets the planned final access scope, merging consecutive readers.</summary>
    /// <param name="resource">A resource imported into the original graph.</param>
    /// <returns>The scope to use for subsequent synchronization after successful execution.</returns>
    public BarrierScope GetFinalScope(RenderGraphResource resource)
    {
        CheckResource(resource);
        return _states.GetValueOrDefault(resource, (resource.InitialState, resource.InitialScope)).Item2;
    }

    /// <summary>Records barriers and callbacks once into a caller-owned command buffer.</summary>
    /// <param name="commands">A recording command buffer outside any active pass.</param>
    /// <remarks>Does not finish, submit, wait or dispose. A failed callback consumes the plan; discard the partial recording.</remarks>
    public void Record(IGraphicsCommandBuffer commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (_recorded || commands.State != CommandBufferState.Recording)
        {
            throw new InvalidOperationException("Plan must be unused and command buffer must be recording.");
        }

        _recorded = true;
        foreach (CompiledPass pass in _passes)
        {
            foreach (Action<IGraphicsCommandBuffer> barrier in pass.Barriers)
            {
                barrier(commands);
            }

            var context = new RenderGraphContext(commands, pass.Uses);
            try
            {
                pass.Callback(context);
                if (commands.State != CommandBufferState.Recording)
                {
                    throw new InvalidOperationException("A graph callback must leave the command buffer recording.");
                }
            }
            finally
            {
                context.Close();
            }
        }
    }

    private static BarrierScope FollowingReaderScope(IReadOnlyList<RenderGraphPass> passes, int passIndex, RenderGraphUse current)
    {
        BarrierScope result = current.Scope;
        if (RenderGraphValidation.IsWrite(current.Scope))
        {
            return result;
        }

        for (int index = passIndex + 1; index < passes.Count; index++)
        {
            foreach (RenderGraphUse use in passes[index].Uses)
            {
                if (use.Resource != current.Resource)
                {
                    continue;
                }

                if (use.State != current.State || RenderGraphValidation.IsWrite(use.Scope))
                {
                    return result;
                }

                result = new BarrierScope(result.Stages | use.Scope.Stages, result.Access | use.Scope.Access);
            }
        }

        return result;
    }

    private void CheckResource(RenderGraphResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.Owner != _owner)
        {
            throw new ArgumentException("Resource belongs to another graph.", nameof(resource));
        }
    }

    private sealed record CompiledPass(Action<RenderGraphContext> Callback, RenderGraphUse[] Uses, List<Action<IGraphicsCommandBuffer>> Barriers);
}
