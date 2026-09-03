namespace Lumyte.Graphics;

internal interface IGpuRenderGraphPassRecorder
{
    void Record(
        GpuCommandBuffer commands,
        IGpuBackend? backend,
        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources,
        IReadOnlySet<GpuRenderGraphResource> allowedResources);
}

internal sealed class GpuRenderGraphNoopPassRecorder : IGpuRenderGraphPassRecorder
{
    public static GpuRenderGraphNoopPassRecorder Instance { get; } = new();

    private GpuRenderGraphNoopPassRecorder() { }

    public void Record(
        GpuCommandBuffer commands,
        IGpuBackend? backend,
        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources,
        IReadOnlySet<GpuRenderGraphResource> allowedResources) { }
}

public enum GpuRenderGraphResourceKind
{
    Texture,
    Buffer,
}

[Flags]
public enum GpuRenderGraphAccess
{
    Read = 1 << 0,
    Write = 1 << 1,
    ReadWrite = Read | Write,
}

[Flags]
public enum GpuRenderGraphPassFlags
{
    None = 0,
    NeverCull = 1 << 0,
}

public readonly record struct GpuRenderGraphResource(int Value)
{
    public bool IsNull => Value == 0;
}

public sealed record GpuRenderGraphResourceInfo(
    GpuRenderGraphResource Resource,
    string Name,
    GpuRenderGraphResourceKind Kind,
    GpuTextureHandle Texture,
    GpuBufferHandle Buffer)
{
    public GpuTextureDescription? TextureDescription { get; internal init; }
    public GpuBufferDescription? BufferDescription { get; internal init; }
    public GpuMemoryKind MemoryKind { get; internal init; } = GpuMemoryKind.DeviceLocal;
    public bool IsTransient { get; internal init; }
    public bool IsExported { get; internal set; }

    internal GpuRenderGraphExportedTexture? ImportedTexture { get; init; }
    internal GpuRenderGraphExportedBuffer? ImportedBuffer { get; init; }
}

public sealed record GpuRenderGraphResourceAccess(
    GpuRenderGraphResource Resource,
    GpuRenderGraphAccess Access,
    GpuStage Stage,
    GpuBarrierHazards Hazards);

public sealed class GpuRenderGraphPassPlan
{
    internal GpuRenderGraphPassPlan(string name, int declarationIndex, GpuRenderGraphResourceAccess[] accesses)
    {
        Name = name;
        DeclarationIndex = declarationIndex;
        Accesses = Array.AsReadOnly(accesses);
    }

    public string Name { get; }
    public int DeclarationIndex { get; }
    public IReadOnlyList<GpuRenderGraphResourceAccess> Accesses { get; }
}

public sealed class GpuRenderGraphBarrierPlan
{
    internal GpuRenderGraphBarrierPlan(
        string destinationPass,
        GpuStage before,
        GpuStage after,
        GpuBarrierHazards hazards,
        GpuRenderGraphResource[] resources)
    {
        DestinationPass = destinationPass;
        Before = before;
        After = after;
        Hazards = hazards;
        Resources = Array.AsReadOnly(resources);
    }

    public string DestinationPass { get; }
    public GpuStage Before { get; }
    public GpuStage After { get; }
    public GpuBarrierHazards Hazards { get; }
    public IReadOnlyList<GpuRenderGraphResource> Resources { get; }
}

/// <summary>
/// Lifetime and logical reuse-slot assignment for one live graph-created resource.
/// A reuse slot is a compile-time plan and does not by itself imply native memory aliasing.
/// </summary>
public sealed record GpuRenderGraphTransientResourcePlan(
    GpuRenderGraphResource Resource,
    GpuTransientLifetime Lifetime,
    int ReuseSlot);

/// <summary>
/// A conservative group of compatible transient resources whose lifetimes do not overlap.
/// Resources are compatible only when their kind, memory kind, and complete description match.
/// </summary>
public sealed class GpuRenderGraphTransientSlotPlan
{
    internal GpuRenderGraphTransientSlotPlan(
        int slot,
        GpuRenderGraphResourceKind kind,
        GpuMemoryKind memoryKind,
        GpuTextureDescription? textureDescription,
        GpuBufferDescription? bufferDescription,
        GpuRenderGraphResource[] resources)
    {
        Slot = slot;
        Kind = kind;
        MemoryKind = memoryKind;
        TextureDescription = textureDescription;
        BufferDescription = bufferDescription;
        Resources = Array.AsReadOnly(resources);
    }

    public int Slot { get; }
    public GpuRenderGraphResourceKind Kind { get; }
    public GpuMemoryKind MemoryKind { get; }
    public GpuTextureDescription? TextureDescription { get; }
    public GpuBufferDescription? BufferDescription { get; }
    public IReadOnlyList<GpuRenderGraphResource> Resources { get; }
}

public sealed class GpuRenderGraphAliasBarrierPlan
{
    internal GpuRenderGraphAliasBarrierPlan(
        string destinationPass,
        int reuseSlot,
        GpuRenderGraphResource beforeResource,
        GpuRenderGraphResource afterResource,
        GpuStage before,
        GpuStage after,
        GpuBarrierHazards hazards)
    {
        DestinationPass = destinationPass;
        ReuseSlot = reuseSlot;
        BeforeResource = beforeResource;
        AfterResource = afterResource;
        Before = before;
        After = after;
        Hazards = hazards;
    }

    public string DestinationPass { get; }
    public int ReuseSlot { get; }
    public GpuRenderGraphResource BeforeResource { get; }
    public GpuRenderGraphResource AfterResource { get; }
    public GpuStage Before { get; }
    public GpuStage After { get; }
    public GpuBarrierHazards Hazards { get; }
}

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

    public IReadOnlyList<GpuRenderGraphResourceInfo> Resources { get; }
    public IReadOnlyList<GpuRenderGraphPassPlan> Passes { get; }
    public IReadOnlyList<GpuRenderGraphBarrierPlan> Barriers { get; }
    public IReadOnlyList<GpuRenderGraphTransientResourcePlan> TransientResources { get; }
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

public sealed partial class GpuRenderGraph
{
    private static int s_nextResourceId;
    private const GpuStage AllStages = GpuStage.DrawIndirect | GpuStage.VertexShader | GpuStage.PixelShader
        | GpuStage.ComputeShader | GpuStage.ColorOutput | GpuStage.DepthStencil | GpuStage.Copy
        | GpuStage.AllGraphics | GpuStage.All;
    private const GpuBarrierHazards AllHazards = GpuBarrierHazards.Descriptors
        | GpuBarrierHazards.IndirectArguments | GpuBarrierHazards.DepthCaches;
    private readonly List<ResourceDeclaration> resources = [];
    private readonly List<PassDeclaration> passes = [];
    private readonly Dictionary<string, GpuRenderGraphResource> resourcesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<(GpuRenderGraphResourceKind Kind, ulong Value), GpuRenderGraphResource> importedResources = [];
    private readonly HashSet<GpuRenderGraphResource> outputs = [];
    private readonly HashSet<string> passNames = new(StringComparer.Ordinal);

    public GpuRenderGraphResource ImportTexture(string name, GpuTextureHandle texture)
        => ImportTexture(name, texture, null);

    public GpuRenderGraphResource ImportTexture(
        string name,
        GpuTextureHandle texture,
        GpuTextureDescription description)
        => ImportTexture(name, texture, (GpuTextureDescription?)description);

    public GpuRenderGraphResource ImportTexture(
        string name,
        GpuRenderGraphExportedTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        texture.RequireAlive();
        return Import(
            name,
            GpuRenderGraphResourceKind.Texture,
            texture.Texture.Value,
            texture.Texture,
            default,
            texture.Description,
            null,
            GpuMemoryKind.DeviceLocal,
            false,
            texture,
            null);
    }

    public GpuRenderGraphResource ImportBuffer(string name, GpuBufferHandle buffer)
        => ImportBuffer(name, buffer, null);

    public GpuRenderGraphResource ImportBuffer(
        string name,
        GpuBufferHandle buffer,
        GpuBufferDescription description)
        => ImportBuffer(name, buffer, (GpuBufferDescription?)description);

    public GpuRenderGraphResource ImportBuffer(
        string name,
        GpuRenderGraphExportedBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.RequireAlive();
        return Import(
            name,
            GpuRenderGraphResourceKind.Buffer,
            buffer.Buffer.Value,
            default,
            buffer.Buffer,
            null,
            buffer.Description,
            GpuMemoryKind.DeviceLocal,
            false,
            null,
            buffer);
    }

    public GpuRenderGraphResource CreateTexture(
        string name,
        GpuTextureDescription description)
    {
        description.Validate();
        return Import(
            name,
            GpuRenderGraphResourceKind.Texture,
            0,
            default,
            default,
            description,
            null,
            GpuMemoryKind.DeviceLocal,
            true,
            null,
            null);
    }

    public GpuRenderGraphResource CreateBuffer(
        string name,
        GpuBufferDescription description,
        GpuMemoryKind memoryKind = GpuMemoryKind.DeviceLocal)
    {
        description.Validate();
        if (!Enum.IsDefined(memoryKind))
        {
            throw new ArgumentOutOfRangeException(nameof(memoryKind));
        }
        return Import(
            name,
            GpuRenderGraphResourceKind.Buffer,
            0,
            default,
            default,
            null,
            description,
            memoryKind,
            true,
            null,
            null);
    }

    /// <summary>
    /// Adds a pass whose static callback receives explicit state and a stack-only context.
    /// This avoids closure and per-record context allocations.
    /// </summary>
    public GpuRenderGraphPassBuilder AddPass<TState>(
        string name,
        TState state,
        GpuRenderGraphPassAction<TState> record,
        GpuRenderGraphPassFlags flags = GpuRenderGraphPassFlags.None)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidatePass(name, flags);
        return AddPass(new StatefulPassDeclaration<TState>(name, state, record, flags));
    }

    private GpuRenderGraphPassBuilder AddPass(PassDeclaration declaration)
    {
        passes.Add(declaration);
        return new(this, declaration);
    }

    private void ValidatePass(string name, GpuRenderGraphPassFlags flags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if ((flags & ~GpuRenderGraphPassFlags.NeverCull) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flags));
        }
        if (!passNames.Add(name))
        {
            throw new ArgumentException($"A pass named '{name}' already exists.", nameof(name));
        }
    }

    public GpuRenderGraph MarkOutput(GpuRenderGraphResource resource)
    {
        RequireResource(resource);
        outputs.Add(resource);
        return this;
    }

    public GpuRenderGraphResource ExportTexture(GpuRenderGraphResource resource)
        => Export(resource, GpuRenderGraphResourceKind.Texture);

    public GpuRenderGraphResource ExportBuffer(GpuRenderGraphResource resource)
        => Export(resource, GpuRenderGraphResourceKind.Buffer);

    public GpuRenderGraphPlan Compile() => CompileUncached();

    public GpuRenderGraphPlan Compile(GpuRenderGraphPlanCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return cache.Compile(this);
    }

    internal GpuRenderGraphPlan CompileUncached()
    {
        var dataDependencies = new HashSet<int>[passes.Count];
        for (int index = 0; index < passes.Count; index++) { dataDependencies[index] = []; }
        var lastWriter = new Dictionary<GpuRenderGraphResource, int>();

        for (int passIndex = 0; passIndex < passes.Count; passIndex++)
        {
            foreach (GpuRenderGraphResourceAccess access in passes[passIndex].Accesses)
            {
                if ((access.Access & GpuRenderGraphAccess.Read) != 0
                    && lastWriter.TryGetValue(access.Resource, out int writer))
                {
                    dataDependencies[passIndex].Add(writer);
                }
                if ((access.Access & GpuRenderGraphAccess.Write) != 0)
                {
                    lastWriter[access.Resource] = passIndex;
                }
            }
        }

        foreach (ResourceDeclaration resource in resources.Where(
            resource => resource.Info.IsExported))
        {
            if (!lastWriter.ContainsKey(resource.Info.Resource))
            {
                throw new InvalidOperationException(
                    $"Exported resource '{resource.Info.Name}' has no writer.");
            }
        }

        var live = new HashSet<int>();
        var pending = new Stack<int>();
        foreach (GpuRenderGraphResource output in outputs)
        {
            if (lastWriter.TryGetValue(output, out int writer)) { pending.Push(writer); }
        }
        for (int index = 0; index < passes.Count; index++)
        {
            if ((passes[index].Flags & GpuRenderGraphPassFlags.NeverCull) != 0) { pending.Push(index); }
        }
        while (pending.TryPop(out int passIndex))
        {
            if (!live.Add(passIndex)) { continue; }
            foreach (int dependency in dataDependencies[passIndex]) { pending.Push(dependency); }
        }

        int[] ordered = OrderLivePasses(live);
        GpuRenderGraphPassPlan[] passPlans = ordered.Select(index => new GpuRenderGraphPassPlan(
            passes[index].Name,
            index,
            [.. passes[index].Accesses])).ToArray();
        IGpuRenderGraphPassRecorder[] recorders = ordered
            .Select(index => (IGpuRenderGraphPassRecorder)passes[index])
            .ToArray();
        GpuRenderGraphBarrierPlan[] barriers = PlanBarriers(ordered);
        (GpuRenderGraphTransientResourcePlan[] transientResources,
            GpuRenderGraphTransientSlotPlan[] transientSlots,
            GpuRenderGraphAliasBarrierPlan[] aliasBarriers) = PlanTransients(ordered);
        GpuRenderGraphResourceInfo[] resourceInfos = resources
            .Select(resource => resource.Info with { })
            .ToArray();
        return new(
            resourceInfos,
            passPlans,
            barriers,
            transientResources,
            transientSlots,
            aliasBarriers,
            recorders);
    }

    internal void AddAccess(
        PassDeclaration pass,
        GpuRenderGraphResource resource,
        GpuRenderGraphAccess access,
        GpuStage stage,
        GpuBarrierHazards hazards)
    {
        RequireResource(resource);
        if (access is not (GpuRenderGraphAccess.Read or GpuRenderGraphAccess.Write or GpuRenderGraphAccess.ReadWrite))
        {
            throw new ArgumentOutOfRangeException(nameof(access));
        }
        if (stage == GpuStage.None || (stage & ~AllStages) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }
        if ((hazards & ~AllHazards) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hazards));
        }
        if (pass.Accesses.Any(candidate => candidate.Resource == resource))
        {
            throw new InvalidOperationException("A pass may declare each resource once; use ReadWrite for combined access.");
        }
        pass.Accesses.Add(new(resource, access, stage, hazards));
    }

    private GpuRenderGraphResource ImportTexture(
        string name,
        GpuTextureHandle texture,
        GpuTextureDescription? description)
    {
        if (texture.IsNull) { throw new ArgumentException("Texture cannot be null.", nameof(texture)); }
        if (description is { } value) { value.Validate(); }
        return Import(
            name,
            GpuRenderGraphResourceKind.Texture,
            texture.Value,
            texture,
            default,
            description,
            null,
            GpuMemoryKind.DeviceLocal,
            false,
            null,
            null);
    }

    private GpuRenderGraphResource ImportBuffer(
        string name,
        GpuBufferHandle buffer,
        GpuBufferDescription? description)
    {
        if (buffer.IsNull) { throw new ArgumentException("Buffer cannot be null.", nameof(buffer)); }
        if (description is { } value) { value.Validate(); }
        return Import(
            name,
            GpuRenderGraphResourceKind.Buffer,
            buffer.Value,
            default,
            buffer,
            null,
            description,
            GpuMemoryKind.DeviceLocal,
            false,
            null,
            null);
    }

    private GpuRenderGraphResource Import(
        string name,
        GpuRenderGraphResourceKind kind,
        ulong nativeValue,
        GpuTextureHandle texture,
        GpuBufferHandle buffer,
        GpuTextureDescription? textureDescription,
        GpuBufferDescription? bufferDescription,
        GpuMemoryKind memoryKind,
        bool isTransient,
        GpuRenderGraphExportedTexture? importedTexture,
        GpuRenderGraphExportedBuffer? importedBuffer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (resourcesByName.ContainsKey(name))
        {
            throw new ArgumentException($"A resource named '{name}' already exists.", nameof(name));
        }
        if (!isTransient && importedResources.ContainsKey((kind, nativeValue)))
        {
            throw new ArgumentException("The native resource is already imported.", nameof(nativeValue));
        }

        var resource = new GpuRenderGraphResource(Interlocked.Increment(ref s_nextResourceId));
        var info = new GpuRenderGraphResourceInfo(resource, name, kind, texture, buffer)
        {
            TextureDescription = textureDescription,
            BufferDescription = bufferDescription,
            MemoryKind = memoryKind,
            IsTransient = isTransient,
            ImportedTexture = importedTexture,
            ImportedBuffer = importedBuffer,
        };
        resources.Add(new(info));
        resourcesByName.Add(name, resource);
        if (!isTransient) { importedResources.Add((kind, nativeValue), resource); }
        return resource;
    }

    private GpuRenderGraphResource Export(
        GpuRenderGraphResource resource,
        GpuRenderGraphResourceKind kind)
    {
        ResourceDeclaration declaration = RequireResource(resource);
        if (declaration.Info.Kind != kind)
        {
            throw new ArgumentException(
                $"Resource is not a {kind.ToString().ToLowerInvariant()}.",
                nameof(resource));
        }
        if (!declaration.Info.IsTransient)
        {
            throw new InvalidOperationException("Only graph-created resources can be exported.");
        }
        declaration.Info.IsExported = true;
        outputs.Add(resource);
        return resource;
    }

    private ResourceDeclaration RequireResource(GpuRenderGraphResource resource)
    {
        ResourceDeclaration? declaration = resources.FirstOrDefault(
            candidate => candidate.Info.Resource == resource);
        if (resource.IsNull || declaration is null)
        {
            throw new ArgumentException("Resource does not belong to this render graph.", nameof(resource));
        }
        return declaration;
    }

    private int[] OrderLivePasses(HashSet<int> live)
    {
        var edges = new HashSet<int>[passes.Count];
        var indegree = new int[passes.Count];
        for (int index = 0; index < passes.Count; index++) { edges[index] = []; }
        var lastWriter = new Dictionary<GpuRenderGraphResource, int>();
        var readers = new Dictionary<GpuRenderGraphResource, List<int>>();

        foreach (int passIndex in Enumerable.Range(0, passes.Count).Where(live.Contains))
        {
            foreach (GpuRenderGraphResourceAccess access in passes[passIndex].Accesses)
            {
                readers.TryGetValue(access.Resource, out List<int>? priorReaders);
                lastWriter.TryGetValue(access.Resource, out int priorWriter);
                bool hasWriter = lastWriter.ContainsKey(access.Resource);
                if ((access.Access & GpuRenderGraphAccess.Read) != 0)
                {
                    if (hasWriter) { AddEdge(priorWriter, passIndex); }
                    priorReaders ??= [];
                    priorReaders.Add(passIndex);
                    readers[access.Resource] = priorReaders;
                }
                if ((access.Access & GpuRenderGraphAccess.Write) != 0)
                {
                    if (hasWriter) { AddEdge(priorWriter, passIndex); }
                    if (priorReaders is not null)
                    {
                        foreach (int reader in priorReaders) { AddEdge(reader, passIndex); }
                        priorReaders.Clear();
                    }
                    lastWriter[access.Resource] = passIndex;
                }
            }
        }

        var ready = new SortedSet<int>(live.Where(index => indegree[index] == 0));
        var result = new List<int>(live.Count);
        while (ready.Count != 0)
        {
            int passIndex = ready.Min;
            ready.Remove(passIndex);
            result.Add(passIndex);
            foreach (int next in edges[passIndex])
            {
                if (--indegree[next] == 0) { ready.Add(next); }
            }
        }
        if (result.Count != live.Count)
        {
            throw new InvalidOperationException("Render graph contains a dependency cycle.");
        }
        return [.. result];

        void AddEdge(int from, int to)
        {
            if (from != to && edges[from].Add(to)) { indegree[to]++; }
        }
    }

    private GpuRenderGraphBarrierPlan[] PlanBarriers(int[] ordered)
    {
        var previous = new Dictionary<GpuRenderGraphResource, GpuRenderGraphResourceAccess>();
        var result = new List<GpuRenderGraphBarrierPlan>();
        foreach (int passIndex in ordered)
        {
            GpuStage before = GpuStage.None;
            GpuStage after = GpuStage.None;
            GpuBarrierHazards hazards = GpuBarrierHazards.None;
            var transitioned = new List<GpuRenderGraphResource>();
            foreach (GpuRenderGraphResourceAccess current in passes[passIndex].Accesses)
            {
                bool requiresBarrier = !previous.TryGetValue(current.Resource, out GpuRenderGraphResourceAccess? prior)
                    || (prior.Access & GpuRenderGraphAccess.Write) != 0
                    || (current.Access & GpuRenderGraphAccess.Write) != 0;
                if (requiresBarrier)
                {
                    before |= prior?.Stage ?? GpuStage.None;
                    after |= current.Stage;
                    hazards |= current.Hazards | (prior?.Hazards ?? GpuBarrierHazards.None);
                    transitioned.Add(current.Resource);
                }
                previous[current.Resource] = current;
            }
            if (transitioned.Count != 0)
            {
                result.Add(new(
                    passes[passIndex].Name,
                    before,
                    after,
                    hazards,
                    [.. transitioned]));
            }
        }
        return [.. result];
    }

    private (
        GpuRenderGraphTransientResourcePlan[] Resources,
        GpuRenderGraphTransientSlotPlan[] Slots,
        GpuRenderGraphAliasBarrierPlan[] AliasBarriers) PlanTransients(int[] ordered)
    {
        var candidates = new List<TransientCandidate>();
        for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
        {
            GpuRenderGraphResourceInfo info = resources[resourceIndex].Info;
            if (!info.IsTransient) { continue; }

            int firstPass = int.MaxValue;
            int lastPass = -1;
            for (int executionIndex = 0; executionIndex < ordered.Length; executionIndex++)
            {
                if (passes[ordered[executionIndex]].Accesses.Any(access => access.Resource == info.Resource))
                {
                    firstPass = Math.Min(firstPass, executionIndex);
                    lastPass = executionIndex;
                }
            }
            if (lastPass < 0) { continue; }
            if (info.IsExported) { lastPass = ordered.Length - 1; }

            candidates.Add(new(
                resourceIndex,
                info,
                new GpuTransientLifetime(firstPass, lastPass)));
        }

        var slots = new List<TransientSlot>();
        var plans = new List<GpuRenderGraphTransientResourcePlan>(candidates.Count);
        foreach (TransientCandidate candidate in candidates
            .OrderBy(candidate => candidate.Lifetime.FirstPass)
            .ThenBy(candidate => candidate.DeclarationIndex))
        {
            TransientSlot? slot = slots.FirstOrDefault(existing =>
                existing.IsCompatible(candidate.Info)
                && existing.Lifetimes.All(lifetime => !lifetime.Overlaps(candidate.Lifetime)));
            if (slot is null)
            {
                slot = new(slots.Count, candidate.Info);
                slots.Add(slot);
            }
            slot.Resources.Add(candidate.Info.Resource);
            slot.Lifetimes.Add(candidate.Lifetime);
            plans.Add(new(candidate.Info.Resource, candidate.Lifetime, slot.Index));
        }

        var aliasBarriers = new List<GpuRenderGraphAliasBarrierPlan>();
        foreach (TransientSlot slot in slots)
        {
            TransientCandidate[] assigned = slot.Resources
                .Select(resource => candidates.Single(candidate => candidate.Info.Resource == resource))
                .OrderBy(candidate => candidate.Lifetime.FirstPass)
                .ToArray();
            for (int index = 1; index < assigned.Length; index++)
            {
                TransientCandidate before = assigned[index - 1];
                TransientCandidate after = assigned[index];
                GpuRenderGraphResourceAccess beforeAccess = passes[ordered[before.Lifetime.LastPass]].Accesses
                    .Single(access => access.Resource == before.Info.Resource);
                GpuRenderGraphResourceAccess afterAccess = passes[ordered[after.Lifetime.FirstPass]].Accesses
                    .Single(access => access.Resource == after.Info.Resource);
                aliasBarriers.Add(new(
                    passes[ordered[after.Lifetime.FirstPass]].Name,
                    slot.Index,
                    before.Info.Resource,
                    after.Info.Resource,
                    beforeAccess.Stage,
                    afterAccess.Stage,
                    beforeAccess.Hazards | afterAccess.Hazards));
            }
        }

        return (
            [.. plans],
            [.. slots.Select(slot => new GpuRenderGraphTransientSlotPlan(
                slot.Index,
                slot.Kind,
                slot.MemoryKind,
                slot.TextureDescription,
                slot.BufferDescription,
                [.. slot.Resources]))],
            [.. aliasBarriers]);
    }

    private sealed record ResourceDeclaration(GpuRenderGraphResourceInfo Info);

    private sealed record TransientCandidate(
        int DeclarationIndex,
        GpuRenderGraphResourceInfo Info,
        GpuTransientLifetime Lifetime);

    private sealed class TransientSlot(int index, GpuRenderGraphResourceInfo resource)
    {
        public int Index { get; } = index;
        public GpuRenderGraphResourceKind Kind { get; } = resource.Kind;
        public GpuMemoryKind MemoryKind { get; } = resource.MemoryKind;
        public GpuTextureDescription? TextureDescription { get; } = resource.TextureDescription;
        public GpuBufferDescription? BufferDescription { get; } = resource.BufferDescription;
        public List<GpuRenderGraphResource> Resources { get; } = [];
        public List<GpuTransientLifetime> Lifetimes { get; } = [];

        public bool IsCompatible(GpuRenderGraphResourceInfo candidate)
            => Kind == candidate.Kind
                && MemoryKind == candidate.MemoryKind
                && TextureDescription == candidate.TextureDescription
                && BufferDescription == candidate.BufferDescription;
    }

    internal abstract class PassDeclaration(
        string name,
        GpuRenderGraphPassFlags flags) : IGpuRenderGraphPassRecorder
    {
        public string Name { get; } = name;
        public GpuRenderGraphPassFlags Flags { get; } = flags;
        public List<GpuRenderGraphResourceAccess> Accesses { get; } = [];

        public abstract void Record(
            GpuCommandBuffer commands,
            IGpuBackend? backend,
            IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources,
            IReadOnlySet<GpuRenderGraphResource> allowedResources);
    }

    private sealed class StatefulPassDeclaration<TState>(
        string name,
        TState state,
        GpuRenderGraphPassAction<TState> record,
        GpuRenderGraphPassFlags flags) : PassDeclaration(name, flags)
    {
        public override void Record(
            GpuCommandBuffer commands,
            IGpuBackend? backend,
            IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources,
            IReadOnlySet<GpuRenderGraphResource> allowedResources)
            => record(new(commands, backend, resources, allowedResources), state);
    }
}

public sealed class GpuRenderGraphPassBuilder
{
    private readonly GpuRenderGraph owner;
    private readonly GpuRenderGraph.PassDeclaration pass;

    internal GpuRenderGraphPassBuilder(GpuRenderGraph owner, GpuRenderGraph.PassDeclaration pass)
    {
        this.owner = owner;
        this.pass = pass;
    }

    public GpuRenderGraphPassBuilder Read(
        GpuRenderGraphResource resource,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, resource, GpuRenderGraphAccess.Read, stage, hazards);
        return this;
    }

    public GpuRenderGraphPassBuilder Write(
        GpuRenderGraphResource resource,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, resource, GpuRenderGraphAccess.Write, stage, hazards);
        return this;
    }

    public GpuRenderGraphPassBuilder ReadWrite(
        GpuRenderGraphResource resource,
        GpuStage stage,
        GpuBarrierHazards hazards = GpuBarrierHazards.None)
    {
        owner.AddAccess(pass, resource, GpuRenderGraphAccess.ReadWrite, stage, hazards);
        return this;
    }
}
