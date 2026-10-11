using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Builds a backend-independent graph over caller-owned allocations.</summary>
/// <remarks>Resource hazards follow declaration order. The caller manages device identity, synchronization and GPU lifetime.</remarks>
public sealed class RenderGraph
{
    private readonly HashSet<object> _allocations = new(ReferenceEqualityComparer.Instance);
    private readonly List<RenderGraphPass> _passes = [];

    /// <summary>Imports a whole texture with its state and accesses before this graph.</summary>
    /// <param name="texture">The caller-owned allocation; duplicate imports are rejected.</param>
    /// <param name="initialState">The current state; Undefined discards contents and requires a first write.</param>
    /// <param name="initialScope">The preceding accesses, or an empty scope for a new allocation.</param>
    /// <returns>A handle valid only in this graph.</returns>
    public RenderGraphTexture ImportTexture(IGraphicsTexture texture, TextureState initialState, BarrierScope initialScope)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var resource = new RenderGraphTexture(this, texture, initialState, initialScope);
        Import(resource);
        return resource;
    }

    /// <summary>Imports a whole typed buffer with the accesses preceding this graph.</summary>
    /// <typeparam name="T">The unmanaged storage element type.</typeparam>
    /// <param name="buffer">The caller-owned allocation with initialized contents if read before a write.</param>
    /// <param name="initialScope">The preceding accesses, or an empty scope for a new allocation.</param>
    /// <returns>A handle valid only in this graph.</returns>
    public RenderGraphBuffer<T> ImportBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope initialScope)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var resource = new RenderGraphBuffer<T>(this, buffer, initialScope);
        Import(resource);
        return resource;
    }

    /// <summary>Adds a render, compute, copy or side-effect recording callback.</summary>
    /// <param name="name">A nonempty diagnostic name.</param>
    /// <param name="record">Records commands without finishing, submitting or waiting.</param>
    /// <returns>The pass on which resource accesses and dependencies are declared.</returns>
    public RenderGraphPass AddPass(string name, Action<RenderGraphContext> record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(record);
        var pass = new RenderGraphPass(this, name, record);
        _passes.Add(pass);
        return pass;
    }

    /// <summary>Snapshots declarations and derives a stable order and barriers without GPU work.</summary>
    /// <returns>A one-shot recording plan independent of later builder changes.</returns>
    /// <exception cref="InvalidOperationException">Dependencies contain a cycle or an undefined texture is read before initialization.</exception>
    public CompiledRenderGraph Compile()
    {
        HashSet<RenderGraphPass>[] dependencies = _passes.Select(pass => new HashSet<RenderGraphPass>(pass.Dependencies)).ToArray();
        for (int later = 0; later < _passes.Count; later++)
        {
            for (int earlier = 0; earlier < later; earlier++)
            {
                if (_passes[earlier].Uses.Any(before => _passes[later].Uses.Any(after =>
                    before.Resource == after.Resource &&
                    (RenderGraphValidation.IsWrite(before.Scope) || RenderGraphValidation.IsWrite(after.Scope) || before.State != after.State))))
                {
                    dependencies[later].Add(_passes[earlier]);
                }
            }
        }

        var ordered = new List<RenderGraphPass>();
        var remaining = new HashSet<RenderGraphPass>(_passes);
        while (remaining.Count > 0)
        {
            int next = _passes.FindIndex(pass => remaining.Contains(pass) && dependencies[_passes.IndexOf(pass)].All(dependency => !remaining.Contains(dependency)));
            if (next < 0)
            {
                throw new InvalidOperationException("Render graph dependencies contain a cycle: " + string.Join(", ", _passes.Where(remaining.Contains).Select(pass => pass.Name)));
            }

            RenderGraphPass selected = _passes[next];
            remaining.Remove(selected);
            ordered.Add(selected);
        }

        return new CompiledRenderGraph(this, ordered);
    }

    private void Import(RenderGraphResource resource)
    {
        RenderGraphValidation.Scope(resource.InitialScope, required: false);
        resource.Validate(resource.InitialState, resource.InitialScope);
        if (!_allocations.Add(resource.Allocation))
        {
            throw new ArgumentException("An allocation may only be imported once into a graph.", nameof(resource));
        }
    }
}
