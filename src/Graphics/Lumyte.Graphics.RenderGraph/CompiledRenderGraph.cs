using System.Diagnostics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Owns temporary allocations and an immutable one-shot recording plan.</summary>
/// <remarks>Dispose after GPU completion, or after discarding the recording command buffer on failure.</remarks>
public sealed class CompiledRenderGraph : IDisposable
{
    private readonly RenderGraph _owner;
    private readonly Dictionary<RenderGraphResource, (TextureState State, BarrierScope Scope)> _states = [];
    private readonly Dictionary<RenderGraphResource, object> _allocations = [];
    private readonly HashSet<RenderGraphResource> _outputs;
    private readonly List<IDisposable> _owned = [];
    private readonly List<IGraphicsTextureView> _views = [];
    private readonly List<CompiledPass> _passes = [];
    private readonly List<PlannedBarrier> _exports = [];
    private List<IRenderGraphLifetime>? _lifetimes;
    private IGraphicsCommandBuffer? _commands;
    private bool _recorded;
    private bool _successful;
    private bool _disposed;

    internal CompiledRenderGraph(RenderGraph owner, RenderGraphPass[] declared, IReadOnlyList<int> order, RenderGraphResource[] resources, HashSet<RenderGraphResource> outputs, RenderGraphUse[] exports, HashSet<int>[] ordering, RenderGraphRun run, RenderGraphCaptureOptions? capture)
    {
        _owner = owner;
        _outputs = outputs;
        Run = run;
        foreach (RenderGraphResource resource in resources)
        {
            _states.Add(resource, (resource.InitialState, resource.InitialScope));
            if (resource.Allocation is object allocation)
            {
                _allocations.Add(resource, allocation);
            }
        }

        RenderGraphPass[] live = order.Select(index => declared[index]).ToArray();
        var initialized = resources.Where(resource => resource.InitiallyInitialized).ToHashSet();
        for (int index = 0; index < live.Length; index++)
        {
            RenderGraphPass pass = live[index];
            var barriers = new List<PlannedBarrier>();
            RenderGraphUse[] uses = pass.Uses.ToArray();
            foreach (RenderGraphUse use in uses)
            {
                bool reads = (use.Scope.Access & ~RenderGraphValidation.Writes) != 0;
                if (reads && !initialized.Contains(use.Resource))
                {
                    throw new InvalidOperationException($"Pass '{pass.Name}' reads uninitialized resource '{use.Resource.Name}'.");
                }

                if (RenderGraphValidation.IsWrite(use.Scope))
                {
                    initialized.Add(use.Resource);
                }

                BarrierScope after = FollowingReaderScope(live, index, use, exports);
                PlanBarrier(use, after, barriers);
            }

            _passes.Add(new CompiledPass(pass.Record, uses, barriers));
        }

        foreach (RenderGraphUse export in exports)
        {
            if (!initialized.Contains(export.Resource))
            {
                throw new InvalidOperationException($"Export '{export.Resource.Name}' is uninitialized.");
            }

            PlanBarrier(export, export.Scope, _exports);
        }

        var execution = order.Select((declaredIndex, index) => (declaredIndex, index)).ToDictionary(item => item.declaredIndex, item => item.index);
        int dependencies = 0;
        foreach (int index in order)
        {
            foreach (int before in ordering[index])
            {
                if (execution.ContainsKey(before))
                {
                    dependencies++;
                }
            }
        }

        Statistics = new RenderGraphStatistics(live.Length, declared.Length - live.Length, resources.Length, resources.Count(resource => resource.IsTransient), dependencies, _passes.Sum(pass => pass.Barriers.Count) + _exports.Count);
        if (capture is not null)
        {
            long estimatedBytes = EstimateSnapshotBytes(declared, resources, Statistics);
            if (estimatedBytes <= capture.MaxBytes)
            {
                Snapshot = CaptureSnapshot(declared, order, resources, ordering, execution, estimatedBytes);
            }
        }

        PassNames = Array.AsReadOnly(live.Select(pass => pass.Name).ToArray());
        CulledPassNames = Array.AsReadOnly(declared.Where((_, index) => !execution.ContainsKey(index)).Select(pass => pass.Name).ToArray());
    }

    /// <summary>Gets pass names in live execution order.</summary>
    public IReadOnlyList<string> PassNames { get; }

    /// <summary>Gets names of passes removed because their outputs were not needed.</summary>
    public IReadOnlyList<string> CulledPassNames { get; }

    /// <summary>Gets the identity shared by compilation, recording and failures.</summary>
    public RenderGraphRun Run { get; }

    /// <summary>Gets counts computed without detailed diagnostic descriptions.</summary>
    public RenderGraphStatistics Statistics { get; }

    /// <summary>Gets requested detached descriptions, or null when not requested or over the capture byte limit.</summary>
    public RenderGraphSnapshot? Snapshot { get; }

    /// <summary>Gets the planned final texture state without querying the GPU.</summary>
    /// <param name="texture">A handle present at compilation.</param>
    /// <returns>The state after successful execution.</returns>
    public TextureState GetFinalTextureState(RenderGraphTexture texture)
    {
        CheckResource(texture);
        return _states[texture].State;
    }

    /// <summary>Gets the planned final scope, including explicit exports.</summary>
    /// <param name="resource">A handle present at compilation.</param>
    /// <returns>The scope for synchronization after successful execution.</returns>
    public BarrierScope GetFinalScope(RenderGraphResource resource)
    {
        CheckResource(resource);
        return _states[resource].Scope;
    }

    /// <summary>Gets a retained texture output after successful recording.</summary>
    /// <param name="texture">The explicitly marked or exported output.</param>
    /// <returns>The allocation, valid until its owner is disposed; GPU completion remains explicit.</returns>
    public IGraphicsTexture GetTexture(RenderGraphTexture texture) => (IGraphicsTexture)GetOutput(texture);

    /// <summary>Gets a retained typed buffer output after successful recording.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="buffer">The explicitly marked or exported output.</param>
    /// <returns>The allocation, valid until its owner is disposed.</returns>
    public IGraphicsBuffer<T> GetBuffer<T>(RenderGraphBuffer<T> buffer)
        where T : unmanaged => (IGraphicsBuffer<T>)GetOutput(buffer);

    /// <summary>Gets a retained imported shader data output after successful recording.</summary>
    /// <typeparam name="T">The application shader data structure.</typeparam>
    /// <param name="buffer">The explicitly marked or exported output.</param>
    /// <returns>The caller-owned allocation.</returns>
    public IGraphicsShaderDataBuffer<T> GetShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer)
        where T : struct, IShaderData => (IGraphicsShaderDataBuffer<T>)GetOutput(buffer);

    /// <summary>Transfers external ownership to this unused plan until safe disposal.</summary>
    /// <param name="lifetime">The ownership validated before recording and released after plan allocations.</param>
    public void Retain(IRenderGraphLifetime lifetime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recorded)
        {
            throw new InvalidOperationException("Retain external ownership before recording.");
        }

        lifetime.Validate();
        (_lifetimes ??= []).Add(lifetime);
    }

    /// <summary>Allocates live temporary resources and records barriers and callbacks once.</summary>
    /// <param name="commands">A same-device recording command buffer outside any active encoder.</param>
    /// <remarks>Does not finish, submit or wait. On failure discard commands before disposing this plan.</remarks>
    public void Record(IGraphicsCommandBuffer commands)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        if (_recorded || commands.State != CommandBufferState.Recording)
        {
            throw new InvalidOperationException("Plan must be unused and command buffer must be recording.");
        }

        if (_lifetimes is { } lifetimes)
        {
            foreach (IRenderGraphLifetime lifetime in lifetimes)
            {
                lifetime.Validate();
            }
        }

        _recorded = true;
        _commands = commands;
        IRenderGraphObserver? observer = _owner.Observer is { IsEnabled: true } ? _owner.Observer : null;
        using IDisposable? diagnostic = observer?.Begin(Run, "record");
        long started = observer is null ? 0 : Stopwatch.GetTimestamp();
        try
        {
            foreach (CompiledPass pass in _passes)
            {
                foreach (RenderGraphUse use in pass.Uses)
                {
                    EnsureAllocation(use.Resource);
                }

                RecordBarriers(commands, pass.Barriers);
                var context = new RenderGraphContext(commands, pass.Uses, this);
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

            RecordBarriers(commands, _exports);
            _successful = true;
            observer?.Recorded(Run, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception exception)
        {
            observer?.Failed(Run, "record", exception);
            throw;
        }
    }

    /// <summary>Disposes owned views and temporary allocations without waiting or disposing imports.</summary>
    /// <exception cref="InvalidOperationException">The recording has not completed or been discarded.</exception>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_commands is not null && _commands.State is not (CommandBufferState.Completed or CommandBufferState.Faulted or CommandBufferState.Disposed))
        {
            throw new InvalidOperationException("Complete GPU work or discard the command buffer before disposing its graph resources.");
        }

        while (_views.Count > 0)
        {
            _views[^1].Dispose();
            _views.RemoveAt(_views.Count - 1);
        }

        while (_owned.Count > 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }

        if (_lifetimes is { } lifetimes)
        {
            while (lifetimes.Count != 0)
            {
                lifetimes[^1].Dispose();
                lifetimes.RemoveAt(lifetimes.Count - 1);
            }
        }

        _allocations.Clear();
        _disposed = true;
    }

    internal object Resolve(RenderGraphResource resource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _allocations[resource];
    }

    internal IGraphicsTextureView CreateView(RenderGraphTexture resource, TextureViewDesc? desc)
    {
        var texture = (IGraphicsTexture)Resolve(resource);
        IGraphicsTextureView view = texture.CreateView(desc);
        _views.Add(view);
        return view;
    }

    private static BarrierScope FollowingReaderScope(IReadOnlyList<RenderGraphPass> passes, int passIndex, RenderGraphUse current, RenderGraphUse[] exports)
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

        foreach (RenderGraphUse export in exports)
        {
            if (export.Resource == current.Resource && export.State == current.State && !RenderGraphValidation.IsWrite(export.Scope))
            {
                result = new BarrierScope(result.Stages | export.Scope.Stages, result.Access | export.Scope.Access);
            }
        }

        return result;
    }

    private static long EstimateSnapshotBytes(RenderGraphPass[] declared, RenderGraphResource[] resources, RenderGraphStatistics statistics)
    {
        // Includes object/array headers, references, wrappers, UTF-16 strings and alignment margin.
        // This is a conservative metadata budget, not an estimate of native allocation size.
        long bytes = 1024 + (96L * declared.Length) + (128L * resources.Length)
            + (32L * statistics.Dependencies) + (112L * statistics.Barriers);
        foreach (RenderGraphPass pass in declared)
        {
            bytes += 2L * pass.Name.Length;
        }

        foreach (RenderGraphResource resource in resources)
        {
            bytes += 2L * (resource.Name.Length + resource.Kind.Length);
        }

        return bytes;
    }

    private RenderGraphSnapshot CaptureSnapshot(RenderGraphPass[] declared, IReadOnlyList<int> order, RenderGraphResource[] resources, HashSet<int>[] ordering, Dictionary<int, int> execution, long estimatedBytes)
    {
        var indexes = resources.Select((resource, index) => (resource, index)).ToDictionary(item => item.resource, item => item.index);
        int[] first = Enumerable.Repeat(-1, resources.Length).ToArray();
        int[] last = Enumerable.Repeat(-1, resources.Length).ToArray();
        var barriers = new RenderGraphBarrierInfo[Statistics.Barriers];
        int barrierIndex = 0;
        for (int passIndex = 0; passIndex < _passes.Count; passIndex++)
        {
            CompiledPass pass = _passes[passIndex];
            foreach (RenderGraphUse use in pass.Uses)
            {
                int resourceIndex = indexes[use.Resource];
                if (first[resourceIndex] == -1)
                {
                    first[resourceIndex] = passIndex;
                }

                last[resourceIndex] = passIndex;
            }

            foreach (PlannedBarrier barrier in pass.Barriers)
            {
                barriers[barrierIndex++] = new RenderGraphBarrierInfo(passIndex, indexes[barrier.Resource], barrier.BeforeState, barrier.AfterState, barrier.Before, barrier.After);
            }
        }

        foreach (PlannedBarrier barrier in _exports)
        {
            barriers[barrierIndex++] = new RenderGraphBarrierInfo(_passes.Count, indexes[barrier.Resource], barrier.BeforeState, barrier.AfterState, barrier.Before, barrier.After);
        }

        RenderGraphPassInfo[] passes = declared.Select((pass, index) => new RenderGraphPassInfo(pass.Name, execution.GetValueOrDefault(index, -1), pass.IsRoot)).ToArray();
        RenderGraphResourceInfo[] resourceInfo = resources.Select((resource, index) => new RenderGraphResourceInfo(resource.Name, resource.Kind, resource.IsTransient, _outputs.Contains(resource), first[index], last[index])).ToArray();
        RenderGraphDependencyInfo[] dependencies = order.SelectMany(index => ordering[index].Where(execution.ContainsKey).Order().Select(before => new RenderGraphDependencyInfo(execution[before], execution[index]))).ToArray();
        return new RenderGraphSnapshot(_owner.Name, estimatedBytes, passes, resourceInfo, dependencies, barriers);
    }

    private void PlanBarrier(RenderGraphUse use, BarrierScope after, List<PlannedBarrier> barriers)
    {
        (TextureState state, BarrierScope before) = _states[use.Resource];
        bool transition = state != use.State;
        bool hazard = before.Access != ResourceAccess.None && (RenderGraphValidation.IsWrite(before) || RenderGraphValidation.IsWrite(after));
        bool expandsReaders = before.Access != ResourceAccess.None &&
            !RenderGraphValidation.IsWrite(before) && !RenderGraphValidation.IsWrite(after) &&
            ((after.Stages & ~before.Stages) != 0 || (after.Access & ~before.Access) != 0);
        if (transition || hazard || expandsReaders)
        {
            barriers.Add(new PlannedBarrier(use.Resource, state, use.State, before, after));
            _states[use.Resource] = (use.State, transition || hazard
                ? after
                : new BarrierScope(before.Stages | after.Stages, before.Access | after.Access));
        }
        else
        {
            _states[use.Resource] = (use.State, new BarrierScope(before.Stages | after.Stages, before.Access | after.Access));
        }
    }

    private void RecordBarriers(IGraphicsCommandBuffer commands, List<PlannedBarrier> barriers)
    {
        foreach (PlannedBarrier barrier in barriers)
        {
            EnsureAllocation(barrier.Resource);
            barrier.Resource.Barrier(commands, Resolve(barrier.Resource), barrier.BeforeState, barrier.AfterState, barrier.Before, barrier.After);
        }
    }

    private void EnsureAllocation(RenderGraphResource resource)
    {
        if (_allocations.ContainsKey(resource))
        {
            return;
        }

        IDisposable allocation = resource.Create(_owner.Device!);
        _owned.Add(allocation);
        _allocations.Add(resource, allocation);
    }

    private object GetOutput(RenderGraphResource resource)
    {
        CheckResource(resource);
        if (!_successful || !_outputs.Contains(resource))
        {
            throw new InvalidOperationException("Only explicit outputs of a successfully recorded plan can be retrieved.");
        }

        return Resolve(resource);
    }

    private void CheckResource(RenderGraphResource resource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(resource);
        if (!_states.ContainsKey(resource))
        {
            throw new ArgumentException("Resource was not present in this compilation.", nameof(resource));
        }
    }

    private sealed record PlannedBarrier(RenderGraphResource Resource, TextureState BeforeState, TextureState AfterState, BarrierScope Before, BarrierScope After);

    private sealed record CompiledPass(Action<RenderGraphContext> Callback, RenderGraphUse[] Uses, List<PlannedBarrier> Barriers);
}
