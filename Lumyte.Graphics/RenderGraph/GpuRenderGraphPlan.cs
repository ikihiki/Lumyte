namespace Lumyte.Graphics.RenderGraph;

public sealed class GpuRenderGraphPlan
{
    private readonly IGpuRenderGraphPassRecorder[] recorders;
    private readonly IReadOnlyDictionary<int, GpuRenderGraphBarrierPlan> barriersByPass;
    private readonly IReadOnlyDictionary<int, GpuRenderGraphAliasBarrierPlan[]> aliasBarriersByPass;
    private readonly bool hasTransientResources;
    private IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime>? importedRuntimes;
    private IReadOnlySet<GpuRenderGraphResource>[]? allowedResourcesByPass;

    internal GpuRenderGraphPlan(
        GpuRenderGraphResourceInfo[] resources,
        GpuRenderGraphPassPlan[] passes,
        GpuRenderGraphBarrierPlan[] barriers,
        GpuRenderGraphTransientResourcePlan[] transientResources,
        GpuRenderGraphTransientSlotPlan[] transientSlots,
        GpuRenderGraphAliasBarrierPlan[] aliasBarriers,
        IGpuRenderGraphPassRecorder[] recorders)
    {
        Resources = Array.AsReadOnly(resources);
        Passes = Array.AsReadOnly(passes);
        Barriers = Array.AsReadOnly(barriers);
        TransientResources = Array.AsReadOnly(transientResources);
        TransientSlots = Array.AsReadOnly(transientSlots);
        AliasBarriers = Array.AsReadOnly(aliasBarriers);
        this.recorders = recorders;
        hasTransientResources = resources.Any(static resource => resource.IsTransient);
        barriersByPass = barriers.ToDictionary(
            barrier => Array.FindIndex(passes, pass => pass.Name == barrier.DestinationPass));
        aliasBarriersByPass = aliasBarriers
            .GroupBy(barrier => Array.FindIndex(passes, pass => pass.Name == barrier.DestinationPass))
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    internal IReadOnlyList<GpuRenderGraphResourceInfo> Resources { get; }
    public IReadOnlyList<GpuRenderGraphPassPlan> Passes { get; }
    public IReadOnlyList<GpuRenderGraphBarrierPlan> Barriers { get; }
    public int TextureCount => Resources.Count(static resource => resource.Kind == GpuRenderGraphResourceKind.Texture);
    public int BufferCount => Resources.Count(static resource => resource.Kind == GpuRenderGraphResourceKind.Buffer);
    public int DependencyCount => Resources.Count(static resource => resource.Kind == GpuRenderGraphResourceKind.Dependency);
    internal IReadOnlyList<GpuRenderGraphTransientResourcePlan> TransientResources { get; }
    public IReadOnlyList<GpuRenderGraphTransientSlotPlan> TransientSlots { get; }
    public IReadOnlyList<GpuRenderGraphAliasBarrierPlan> AliasBarriers { get; }

    /// <summary>Specializes transient placement using this backend's native memory requirements.</summary>
    public GpuRenderGraphMemoryPlan CreateMemoryPlan(IGpuBackend backend)
        => GpuRenderGraphMemoryPlan.Create(this, backend);

    public GpuCommandBuffer Record(IGpuQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        if (hasTransientResources)
        {
            throw new InvalidOperationException(
                "Plans with transient resources require Execute(IGpuBackend).");
        }

        GpuCommandBuffer commands = queue.StartCommandRecording();
        RecordCommands(commands, null, GetImportedRuntimes());
        return commands;
    }

    public GpuRenderGraphExecution Execute(IGpuBackend backend)
        => Execute(backend, null, ownsArena: true, null);

    public GpuRenderGraphExecution Execute(IGpuBackend backend, GpuPersistentArena arena)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(arena);
        arena.RequireBackend(backend);
        return Execute(backend, arena, ownsArena: false, null);
    }

    /// <summary>
    /// Submits the graph without waiting for the GPU. Transient resources are retired through
    /// <paramref name="retirementQueue"/> after the returned completion token finishes.
    /// </summary>
    public GpuRenderGraphExecution ExecuteAsync(
        IGpuBackend backend,
        GpuRetirementQueue retirementQueue)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(retirementQueue);
        retirementQueue.RequireBackend(backend);
        return Execute(backend, null, ownsArena: true, retirementQueue);
    }

    /// <summary>Submits the graph without waiting, using a caller-owned persistent arena.</summary>
    public GpuRenderGraphExecution ExecuteAsync(
        IGpuBackend backend,
        GpuPersistentArena arena,
        GpuRetirementQueue retirementQueue)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(arena);
        ArgumentNullException.ThrowIfNull(retirementQueue);
        arena.RequireBackend(backend);
        retirementQueue.RequireBackend(backend);
        return Execute(backend, arena, ownsArena: false, retirementQueue);
    }

    private GpuRenderGraphExecution Execute(
        IGpuBackend backend,
        GpuPersistentArena? arena,
        bool ownsArena,
        GpuRetirementQueue? retirementQueue)
    {
        ArgumentNullException.ThrowIfNull(backend);
        var runtimes = new Dictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime>();
        var slotAllocations = new Dictionary<int, GpuMemoryAllocation>();
        var releasedSlots = new HashSet<int>();
        var importLeases = new List<IDisposable>();
        bool ownershipTransferred = false;
        try
        {
            HashSet<GpuRenderGraphResource> required = Passes
                .SelectMany(pass => pass.Accesses)
                .Select(access => access.Resource)
                .Concat(Resources
                    .Where(resource => resource.IsExported)
                    .Select(resource => resource.Resource))
                .ToHashSet();
            bool usesPlacedResources = (backend.Capabilities
                    & (GpuBackendCapabilities.ExplicitPlacement | GpuBackendCapabilities.MemoryAliasing))
                == (GpuBackendCapabilities.ExplicitPlacement | GpuBackendCapabilities.MemoryAliasing)
                && TransientResources.Count != 0;
            GpuRenderGraphMemoryPlan? memoryPlan = null;
            if (usesPlacedResources)
            {
                memoryPlan = CreateMemoryPlan(backend);
                arena ??= new(backend);
                foreach (GpuRenderGraphPhysicalSlotPlan slot in memoryPlan.Slots)
                {
                    slotAllocations.Add(
                        slot.Slot,
                        arena.Allocate(
                            slot.Size,
                            slot.Alignment,
                            slot.MemoryKind,
                            slot.Compatibility));
                }
            }
            IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphPhysicalResourcePlan>? physicalResources =
                memoryPlan?.Resources.ToDictionary(resource => resource.Resource);
            foreach (GpuRenderGraphResourceInfo resource in Resources.Where(
                resource => required.Contains(resource.Resource)))
            {
                if (resource.ImportedTexture is { } importedTexture)
                {
                    importLeases.Add(importedTexture.AcquireImportLease(backend));
                }
                if (resource.ImportedBuffer is { } importedBuffer)
                {
                    importLeases.Add(importedBuffer.AcquireImportLease(backend));
                }
                GpuRenderGraphResourceRuntime runtime = resource.IsTransient
                    ? usesPlacedResources
                        ? GpuRenderGraphResourceRuntime.Create(
                            backend,
                            resource,
                            slotAllocations[physicalResources![resource.Resource].ReuseSlot])
                        : GpuRenderGraphResourceRuntime.Create(backend, resource)
                    : GpuRenderGraphResourceRuntime.Import(resource);
                runtimes.Add(resource.Resource, runtime);
            }

            IGpuQueue queue = backend.MainQueue;
            GpuCommandBuffer commands = queue.StartCommandRecording();
            IReadOnlyDictionary<string, int> passIndices = Passes
                .Select((pass, index) => (pass.Name, index))
                .ToDictionary(pair => pair.Name, pair => pair.index, StringComparer.Ordinal);
            IReadOnlyDictionary<int, GpuRenderGraphAliasBarrierPlan[]>? physicalAliasBarriers =
                memoryPlan?.AliasBarriers
                    .GroupBy(barrier => passIndices[barrier.DestinationPass])
                    .ToDictionary(group => group.Key, group => group.ToArray());
            RecordCommands(commands, backend, runtimes, physicalAliasBarriers);
            Dictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> exported =
                runtimes
                    .Where(pair => pair.Value.Info.IsExported)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            GpuRenderGraphResourceRuntime[] nonExported = runtimes.Values
                .Where(runtime => !runtime.Info.IsExported)
                .ToArray();
            var retainedAllocations = new List<GpuMemoryAllocation>();
            var releasableAllocations = new List<(int Slot, GpuMemoryAllocation Allocation)>();
            if (arena is not null)
            {
                HashSet<int> exportedSlots = (physicalResources?.Values
                        ?? Enumerable.Empty<GpuRenderGraphPhysicalResourcePlan>())
                    .Where(plan => exported.ContainsKey(plan.Resource))
                    .Select(plan => plan.ReuseSlot)
                    .ToHashSet();
                foreach ((int slot, GpuMemoryAllocation allocation) in slotAllocations)
                {
                    if (exportedSlots.Contains(slot)) { retainedAllocations.Add(allocation); }
                    else { releasableAllocations.Add((slot, allocation)); }
                }
            }

            if (retirementQueue is null)
            {
                using GpuSemaphore completion = queue.CreateSemaphore();
                queue.Submit([commands], completion, 1);
                queue.Wait(completion, 1);
                DestroyViews(backend, runtimes.Values);
                foreach (GpuRenderGraphResourceRuntime runtime in nonExported)
                {
                    runtime.Dispose(backend);
                }
                if (arena is not null)
                {
                    foreach ((int slot, GpuMemoryAllocation allocation) in releasableAllocations)
                    {
                        arena.Release(allocation);
                        releasedSlots.Add(slot);
                    }
                    if (retainedAllocations.Count == 0)
                    {
                        if (ownsArena) { arena.Dispose(); }
                        arena = null;
                    }
                }
                foreach (IDisposable lease in importLeases) { lease.Dispose(); }
                importLeases.Clear();
                return new(backend, exported, arena, [.. retainedAllocations], ownsArena);
            }

            GpuRenderGraphResourceRuntime[] allRuntimes = runtimes.Values.ToArray();
            GpuPersistentArena? submittedArena = arena;
            var completionActions = new List<Action>();
            foreach (GpuRenderGraphResourceRuntime runtime in allRuntimes)
            {
                if (runtime.View is not null)
                {
                    completionActions.Add(() => DestroyView(backend, runtime));
                }
            }
            foreach (GpuRenderGraphResourceRuntime runtime in nonExported)
            {
                completionActions.Add(() => runtime.Dispose(backend));
            }
            if (submittedArena is not null)
            {
                foreach ((int _, GpuMemoryAllocation allocation) in releasableAllocations)
                {
                    completionActions.Add(() => submittedArena.Release(allocation));
                }
                if (ownsArena && retainedAllocations.Count == 0)
                {
                    completionActions.Add(submittedArena.Dispose);
                }
            }
            foreach (IDisposable lease in importLeases)
            {
                completionActions.Add(lease.Dispose);
            }
            GpuSubmissionToken submission = retirementQueue.Submit(commands, completionActions);
            ownershipTransferred = true;
            if (retainedAllocations.Count == 0) { arena = null; }
            return new(
                backend,
                exported,
                arena,
                [.. retainedAllocations],
                ownsArena,
                retirementQueue,
                submission);
        }
        catch
        {
            if (ownershipTransferred) { throw; }
            DestroyViews(backend, runtimes.Values);
            foreach (GpuRenderGraphResourceRuntime runtime in runtimes.Values)
            {
                runtime.Dispose(backend);
            }
            if (arena is not null)
            {
                foreach ((int slot, GpuMemoryAllocation allocation) in slotAllocations)
                {
                    if (!releasedSlots.Contains(slot)) { arena.Release(allocation); }
                }
                if (ownsArena) { arena.Dispose(); }
            }
            foreach (IDisposable lease in importLeases) { lease.Dispose(); }
            throw;
        }
    }

    private static void DestroyViews(
        IGpuBackend backend,
        IEnumerable<GpuRenderGraphResourceRuntime> runtimes)
    {
        foreach (GpuRenderGraphResourceRuntime runtime in runtimes)
        {
            if (runtime.View is { } view)
            {
                backend.DestroyTextureView(view);
                runtime.View = null;
            }
        }
    }

    private static void DestroyView(
        IGpuBackend backend,
        GpuRenderGraphResourceRuntime runtime)
    {
        if (runtime.View is not { } view) { return; }
        backend.DestroyTextureView(view);
        runtime.View = null;
    }

    private void RecordCommands(
        GpuCommandBuffer commands,
        IGpuBackend? backend,
        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> runtimes,
        IReadOnlyDictionary<int, GpuRenderGraphAliasBarrierPlan[]>? physicalAliasBarriers = null)
    {
        IReadOnlyDictionary<int, GpuRenderGraphAliasBarrierPlan[]> aliasesByPass =
            physicalAliasBarriers ?? aliasBarriersByPass;
        IReadOnlySet<GpuRenderGraphResource>[] allowedResources = GetAllowedResources();
        for (int index = 0; index < Passes.Count; index++)
        {
            if (backend is not null
                && (backend.Capabilities
                        & (GpuBackendCapabilities.ExplicitPlacement | GpuBackendCapabilities.MemoryAliasing))
                    == (GpuBackendCapabilities.ExplicitPlacement | GpuBackendCapabilities.MemoryAliasing)
                && aliasesByPass.TryGetValue(index, out GpuRenderGraphAliasBarrierPlan[]? aliasBarriers))
            {
                foreach (GpuRenderGraphAliasBarrierPlan alias in aliasBarriers)
                {
                    commands.AliasingBarrier(
                        AliasingResource(runtimes[alias.BeforeResource]),
                        AliasingResource(runtimes[alias.AfterResource]),
                        alias.Before,
                        alias.After,
                        alias.Hazards);
                }
            }
            if (barriersByPass.TryGetValue(index, out GpuRenderGraphBarrierPlan? barrier))
            {
                commands.Barrier(barrier.Before, barrier.After, barrier.Hazards);
            }
            recorders[index].Record(commands, backend, runtimes, allowedResources[index]);
        }
    }

    private IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> GetImportedRuntimes()
    {
        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime>? current =
            Volatile.Read(ref importedRuntimes);
        if (current is not null) { return current; }

        var created = new Dictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime>(Resources.Count);
        foreach (GpuRenderGraphResourceInfo resource in Resources)
        {
            created.Add(resource.Resource, GpuRenderGraphResourceRuntime.Import(resource));
        }
        return Interlocked.CompareExchange(ref importedRuntimes, created, null) ?? created;
    }

    private IReadOnlySet<GpuRenderGraphResource>[] GetAllowedResources()
    {
        IReadOnlySet<GpuRenderGraphResource>[]? current = Volatile.Read(ref allowedResourcesByPass);
        if (current is not null) { return current; }

        var created = new IReadOnlySet<GpuRenderGraphResource>[Passes.Count];
        for (int index = 0; index < Passes.Count; index++)
        {
            var resources = new HashSet<GpuRenderGraphResource>();
            foreach (GpuRenderGraphResourceAccess access in Passes[index].Accesses)
            {
                resources.Add(access.Resource);
            }
            created[index] = resources;
        }
        return Interlocked.CompareExchange(ref allowedResourcesByPass, created, null) ?? created;
    }

    private static GpuAliasingResource AliasingResource(GpuRenderGraphResourceRuntime runtime)
        => runtime.Info.Kind == GpuRenderGraphResourceKind.Texture
            ? GpuAliasingResource.FromTexture(runtime.Texture)
            : GpuAliasingResource.FromBuffer(runtime.Buffer);
}
