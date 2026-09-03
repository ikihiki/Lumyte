namespace Lumyte.Graphics;

/// <summary>
/// Collects independently registered render-graph contributions and builds them in a deterministic order.
/// </summary>
public sealed class GpuRenderGraphFrameBuilder
{
    private readonly object sync = new();
    private readonly List<Contribution> contributions = [];
    private readonly HashSet<string> names = new(StringComparer.Ordinal);

    public int ContributionCount
    {
        get { lock (sync) { return contributions.Count; } }
    }

    /// <summary>Adds a contributor whose callback receives explicit state.</summary>
    public GpuRenderGraphFrameBuilder AddContributor<TState>(
        string name,
        TState state,
        Action<GpuRenderGraphContributionContext, TState> contribute,
        int order = 0,
        bool enabled = true)
    {
        ValidateNamespace(name);
        ArgumentNullException.ThrowIfNull(contribute);
        lock (sync)
        {
            if (!names.Add(name))
            {
                throw new ArgumentException(
                    $"A render-graph contributor named '{name}' is already registered.",
                    nameof(name));
            }
            contributions.Add(new StatefulContribution<TState>(
                name,
                order,
                enabled,
                state,
                contribute));
        }
        return this;
    }

    /// <summary>Runs the registration phase and returns a graph ready to compile.</summary>
    public GpuRenderGraph BuildGraph()
    {
        Contribution[] snapshot;
        lock (sync) { snapshot = [.. contributions]; }
        Array.Sort(snapshot, static (left, right) =>
        {
            int order = left.Order.CompareTo(right.Order);
            return order != 0
                ? order
                : StringComparer.Ordinal.Compare(left.Name, right.Name);
        });

        var graph = new GpuRenderGraph();
        var sharedResources = new Dictionary<string, GpuRenderGraphResource>(StringComparer.Ordinal);
        foreach (Contribution contribution in snapshot)
        {
            if (!contribution.Enabled) { continue; }
            var context = new GpuRenderGraphContributionContext(
                graph,
                contribution.Name,
                sharedResources);
            try { contribution.Invoke(context); }
            finally { context.Close(); }
        }
        return graph;
    }

    public GpuRenderGraphPlan Compile() => BuildGraph().Compile();

    public GpuRenderGraphPlan Compile(GpuRenderGraphPlanCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return BuildGraph().Compile(cache);
    }

    private static void ValidateNamespace(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains("::", StringComparison.Ordinal))
        {
            throw new ArgumentException("Contributor names cannot contain '::'.", nameof(name));
        }
    }

    private abstract class Contribution(string name, int order, bool enabled)
    {
        public string Name { get; } = name;
        public int Order { get; } = order;
        public bool Enabled { get; } = enabled;
        public abstract void Invoke(GpuRenderGraphContributionContext context);
    }

    private sealed class StatefulContribution<TState>(
        string name,
        int order,
        bool enabled,
        TState state,
        Action<GpuRenderGraphContributionContext, TState> contribute) : Contribution(name, order, enabled)
    {
        public override void Invoke(GpuRenderGraphContributionContext context) => contribute(context, state);
    }
}

/// <summary>A namespaced registration view over one frame's render graph.</summary>
public sealed class GpuRenderGraphContributionContext
{
    private readonly GpuRenderGraph graph;
    private readonly IReadOnlyDictionary<string, GpuRenderGraphResource> readableSharedResources;
    private readonly Dictionary<string, GpuRenderGraphResource> sharedResources;
    private bool open = true;

    internal GpuRenderGraphContributionContext(
        GpuRenderGraph graph,
        string name,
        Dictionary<string, GpuRenderGraphResource> sharedResources)
    {
        this.graph = graph;
        Name = name;
        this.sharedResources = sharedResources;
        readableSharedResources = sharedResources;
    }

    public string Name { get; }

    public GpuRenderGraphResource ImportTexture(string name, GpuTextureHandle texture)
        => graph.ImportTexture(Qualify(name), texture);

    public GpuRenderGraphResource ImportTexture(
        string name,
        GpuTextureHandle texture,
        GpuTextureDescription description)
        => graph.ImportTexture(Qualify(name), texture, description);

    public GpuRenderGraphResource ImportTexture(
        string name,
        GpuRenderGraphExportedTexture texture)
        => graph.ImportTexture(Qualify(name), texture);

    public GpuRenderGraphResource ImportBuffer(string name, GpuBufferHandle buffer)
        => graph.ImportBuffer(Qualify(name), buffer);

    public GpuRenderGraphResource ImportBuffer(
        string name,
        GpuBufferHandle buffer,
        GpuBufferDescription description)
        => graph.ImportBuffer(Qualify(name), buffer, description);

    public GpuRenderGraphResource ImportBuffer(
        string name,
        GpuRenderGraphExportedBuffer buffer)
        => graph.ImportBuffer(Qualify(name), buffer);

    public GpuRenderGraphResource CreateTexture(
        string name,
        GpuTextureDescription description)
        => graph.CreateTexture(Qualify(name), description);

    public GpuRenderGraphResource CreateBuffer(
        string name,
        GpuBufferDescription description,
        GpuMemoryKind memoryKind = GpuMemoryKind.DeviceLocal)
        => graph.CreateBuffer(Qualify(name), description, memoryKind);

    public GpuRenderGraphPassBuilder AddPass<TState>(
        string name,
        TState state,
        GpuRenderGraphPassAction<TState> record,
        GpuRenderGraphPassFlags flags = GpuRenderGraphPassFlags.None)
        => graph.AddPass(Qualify(name), state, record, flags);

    public GpuRenderGraphContributionContext MarkOutput(GpuRenderGraphResource resource)
    {
        VerifyOpen();
        graph.MarkOutput(resource);
        return this;
    }

    public GpuRenderGraphResource ExportTexture(GpuRenderGraphResource resource)
    {
        VerifyOpen();
        return graph.ExportTexture(resource);
    }

    public GpuRenderGraphResource ExportBuffer(GpuRenderGraphResource resource)
    {
        VerifyOpen();
        return graph.ExportBuffer(resource);
    }

    /// <summary>Publishes a resource for contributors that execute later in registration order.</summary>
    public GpuRenderGraphResource PublishResource(
        string name,
        GpuRenderGraphResource resource)
    {
        VerifyOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        graph.RequireOwnedResource(resource);
        if (!sharedResources.TryAdd(name, resource))
        {
            throw new ArgumentException(
                $"A shared render-graph resource named '{name}' is already published.",
                nameof(name));
        }
        return resource;
    }

    public GpuRenderGraphResource GetResource(string name)
    {
        VerifyOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return readableSharedResources.TryGetValue(name, out GpuRenderGraphResource resource)
            ? resource
            : throw new KeyNotFoundException(
                $"Shared render-graph resource '{name}' has not been published by an earlier contributor.");
    }

    internal void Close() => open = false;

    private string Qualify(string localName)
    {
        VerifyOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(localName);
        if (localName.Contains("::", StringComparison.Ordinal))
        {
            throw new ArgumentException("Local render-graph names cannot contain '::'.", nameof(localName));
        }
        return $"{Name}::{localName}";
    }

    private void VerifyOpen()
        => ObjectDisposedException.ThrowIf(!open, this);
}

/// <summary>
/// Caches immutable render-graph structure while rebinding frame callbacks and imported resources on every hit.
/// </summary>
public sealed class GpuRenderGraphPlanCache
{
    private readonly object sync = new();
    private readonly Dictionary<ulong, List<Entry>> plans = [];
    private readonly Queue<Entry> insertionOrder = [];
    private int count;
    private long hitCount;
    private long missCount;

    public GpuRenderGraphPlanCache(int maximumEntries = 64)
    {
        if (maximumEntries <= 0) { throw new ArgumentOutOfRangeException(nameof(maximumEntries)); }
        MaximumEntries = maximumEntries;
    }

    public int MaximumEntries { get; }
    public int Count { get { lock (sync) { return count; } } }
    public long HitCount { get { lock (sync) { return hitCount; } } }
    public long MissCount { get { lock (sync) { return missCount; } } }

    public GpuRenderGraphPlan Compile(GpuRenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ulong hash = graph.CreateStructureHash();
        lock (sync)
        {
            if (plans.TryGetValue(hash, out List<Entry>? bucket))
            {
                foreach (Entry candidate in bucket)
                {
                    if (!graph.MatchesStructure(candidate.Structure)) { continue; }
                    hitCount++;
                    return graph.BindCachedPlan(candidate.Template, candidate.TemplateResourceIndices);
                }
            }

            GpuRenderGraphPlan plan = graph.CompileUncached();
            if (count == MaximumEntries)
            {
                Entry evicted = insertionOrder.Dequeue();
                List<Entry> evictedBucket = plans[evicted.Structure.Hash];
                evictedBucket.Remove(evicted);
                if (evictedBucket.Count == 0) { plans.Remove(evicted.Structure.Hash); }
                count--;
            }
            GpuRenderGraphPlan template = CreateTemplate(plan);
            var entry = new Entry(
                graph.CaptureStructure(hash),
                template,
                template.Resources
                    .Select((resource, index) => (resource.Resource, index))
                    .ToDictionary(static pair => pair.Resource, static pair => pair.index));
            if (!plans.TryGetValue(hash, out bucket))
            {
                bucket = [];
                plans.Add(hash, bucket);
            }
            bucket.Add(entry);
            insertionOrder.Enqueue(entry);
            count++;
            missCount++;
            return plan;
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            plans.Clear();
            insertionOrder.Clear();
            count = 0;
        }
    }

    private static GpuRenderGraphPlan CreateTemplate(GpuRenderGraphPlan plan)
    {
        GpuRenderGraphResourceInfo[] resources = plan.Resources.Select(info => info with
        {
            Texture = default,
            Buffer = default,
            ImportedTexture = null,
            ImportedBuffer = null,
        }).ToArray();
        IGpuRenderGraphPassRecorder noop = GpuRenderGraphNoopPassRecorder.Instance;
        return new(
            resources,
            [.. plan.Passes],
            [.. plan.Barriers],
            [.. plan.TransientResources],
            [.. plan.TransientSlots],
            [.. plan.AliasBarriers],
            Enumerable.Repeat(noop, plan.Passes.Count).ToArray());
    }

    private sealed record Entry(
        GpuRenderGraphStructure Structure,
        GpuRenderGraphPlan Template,
        IReadOnlyDictionary<GpuRenderGraphResource, int> TemplateResourceIndices);
}

internal sealed class GpuRenderGraphStructure(
    ulong hash,
    GpuRenderGraphResourceStructure[] resources,
    GpuRenderGraphPassStructure[] passes)
{
    public ulong Hash { get; } = hash;
    public GpuRenderGraphResourceStructure[] Resources { get; } = resources;
    public GpuRenderGraphPassStructure[] Passes { get; } = passes;
}

internal readonly record struct GpuRenderGraphResourceStructure(
    string Name,
    GpuRenderGraphResourceKind Kind,
    GpuMemoryKind MemoryKind,
    bool IsTransient,
    bool IsExported,
    bool IsOutput,
    GpuTextureDescription? TextureDescription,
    GpuBufferDescription? BufferDescription);

internal sealed record GpuRenderGraphPassStructure(
    string Name,
    GpuRenderGraphPassFlags Flags,
    GpuRenderGraphAccessStructure[] Accesses);

internal readonly record struct GpuRenderGraphAccessStructure(
    int ResourceIndex,
    GpuRenderGraphAccess Access,
    GpuStage Stage,
    GpuBarrierHazards Hazards);

public sealed partial class GpuRenderGraph
{
    private Dictionary<GpuRenderGraphResource, int>? structureResourceIndices;

    internal void RequireOwnedResource(GpuRenderGraphResource resource) => RequireResource(resource);

    internal ulong CreateStructureHash()
    {
        var hash = new StructureHasher();
        hash.Add(resources.Count);
        foreach (ResourceDeclaration declaration in resources)
        {
            GpuRenderGraphResourceInfo info = declaration.Info;
            hash.Add(info.Name);
            hash.Add((int)info.Kind);
            hash.Add((int)info.MemoryKind);
            hash.Add(info.IsTransient);
            hash.Add(info.IsExported);
            hash.Add(outputs.Contains(info.Resource));
            if (info.TextureDescription is { } texture)
            {
                hash.Add(true);
                hash.Add(texture.Width);
                hash.Add(texture.Height);
                hash.Add((int)texture.Format);
                hash.Add((int)texture.Usage);
                hash.Add(texture.MipCount);
                hash.Add(texture.LayerCount);
                hash.Add(texture.SampleCount);
            }
            else { hash.Add(false); }
            if (info.BufferDescription is { } buffer)
            {
                hash.Add(true);
                hash.Add(buffer.Size);
                hash.Add((int)buffer.Usage);
            }
            else { hash.Add(false); }
        }

        hash.Add(passes.Count);
        foreach (PassDeclaration pass in passes)
        {
            hash.Add(pass.Name);
            hash.Add((int)pass.Flags);
            hash.Add(pass.Accesses.Count);
            foreach (GpuRenderGraphResourceAccess access in pass.Accesses)
            {
                hash.Add(GetStructureResourceIndex(access.Resource));
                hash.Add((int)access.Access);
                hash.Add((uint)access.Stage);
                hash.Add((int)access.Hazards);
            }
        }
        return hash.Value;
    }

    internal GpuRenderGraphStructure CaptureStructure(ulong hash)
    {
        var resourceStructures = new GpuRenderGraphResourceStructure[resources.Count];
        for (int index = 0; index < resources.Count; index++)
        {
            GpuRenderGraphResourceInfo info = resources[index].Info;
            resourceStructures[index] = new(
                info.Name,
                info.Kind,
                info.MemoryKind,
                info.IsTransient,
                info.IsExported,
                outputs.Contains(info.Resource),
                info.TextureDescription,
                info.BufferDescription);
        }

        var passStructures = new GpuRenderGraphPassStructure[passes.Count];
        for (int passIndex = 0; passIndex < passes.Count; passIndex++)
        {
            PassDeclaration pass = passes[passIndex];
            var accesses = new GpuRenderGraphAccessStructure[pass.Accesses.Count];
            for (int accessIndex = 0; accessIndex < pass.Accesses.Count; accessIndex++)
            {
                GpuRenderGraphResourceAccess access = pass.Accesses[accessIndex];
                accesses[accessIndex] = new(
                    GetStructureResourceIndex(access.Resource),
                    access.Access,
                    access.Stage,
                    access.Hazards);
            }
            passStructures[passIndex] = new(pass.Name, pass.Flags, accesses);
        }
        return new(hash, resourceStructures, passStructures);
    }

    internal bool MatchesStructure(GpuRenderGraphStructure structure)
    {
        if (resources.Count != structure.Resources.Length || passes.Count != structure.Passes.Length)
        {
            return false;
        }
        for (int index = 0; index < resources.Count; index++)
        {
            GpuRenderGraphResourceInfo info = resources[index].Info;
            GpuRenderGraphResourceStructure expected = structure.Resources[index];
            if (!StringComparer.Ordinal.Equals(info.Name, expected.Name)
                || info.Kind != expected.Kind
                || info.MemoryKind != expected.MemoryKind
                || info.IsTransient != expected.IsTransient
                || info.IsExported != expected.IsExported
                || outputs.Contains(info.Resource) != expected.IsOutput
                || info.TextureDescription != expected.TextureDescription
                || info.BufferDescription != expected.BufferDescription)
            {
                return false;
            }
        }
        for (int passIndex = 0; passIndex < passes.Count; passIndex++)
        {
            PassDeclaration pass = passes[passIndex];
            GpuRenderGraphPassStructure expected = structure.Passes[passIndex];
            if (!StringComparer.Ordinal.Equals(pass.Name, expected.Name)
                || pass.Flags != expected.Flags
                || pass.Accesses.Count != expected.Accesses.Length)
            {
                return false;
            }
            for (int accessIndex = 0; accessIndex < pass.Accesses.Count; accessIndex++)
            {
                GpuRenderGraphResourceAccess access = pass.Accesses[accessIndex];
                GpuRenderGraphAccessStructure expectedAccess = expected.Accesses[accessIndex];
                if (GetStructureResourceIndex(access.Resource) != expectedAccess.ResourceIndex
                    || access.Access != expectedAccess.Access
                    || access.Stage != expectedAccess.Stage
                    || access.Hazards != expectedAccess.Hazards)
                {
                    return false;
                }
            }
        }
        return true;
    }

    internal GpuRenderGraphPlan BindCachedPlan(
        GpuRenderGraphPlan template,
        IReadOnlyDictionary<GpuRenderGraphResource, int> templateResourceIndices)
    {
        if (template.Resources.Count != resources.Count)
        {
            throw new InvalidOperationException("Cached render-graph structure does not match this frame.");
        }

        GpuRenderGraphResource Map(GpuRenderGraphResource resource)
            => resources[templateResourceIndices[resource]].Info.Resource;

        var passPlans = new GpuRenderGraphPassPlan[template.Passes.Count];
        var currentRecorders = new IGpuRenderGraphPassRecorder[passPlans.Length];
        for (int index = 0; index < passPlans.Length; index++)
        {
            GpuRenderGraphPassPlan pass = template.Passes[index];
            var accesses = new GpuRenderGraphResourceAccess[pass.Accesses.Count];
            for (int accessIndex = 0; accessIndex < accesses.Length; accessIndex++)
            {
                GpuRenderGraphResourceAccess access = pass.Accesses[accessIndex];
                accesses[accessIndex] = access with { Resource = Map(access.Resource) };
            }
            passPlans[index] = new(pass.Name, pass.DeclarationIndex, accesses);
            currentRecorders[index] = passes[pass.DeclarationIndex];
        }

        var barriers = new GpuRenderGraphBarrierPlan[template.Barriers.Count];
        for (int index = 0; index < barriers.Length; index++)
        {
            GpuRenderGraphBarrierPlan barrier = template.Barriers[index];
            var barrierResources = new GpuRenderGraphResource[barrier.Resources.Count];
            for (int resourceIndex = 0; resourceIndex < barrierResources.Length; resourceIndex++)
            {
                barrierResources[resourceIndex] = Map(barrier.Resources[resourceIndex]);
            }
            barriers[index] = new(
                barrier.DestinationPass,
                barrier.Before,
                barrier.After,
                barrier.Hazards,
                barrierResources);
        }

        var transientResources = new GpuRenderGraphTransientResourcePlan[template.TransientResources.Count];
        for (int index = 0; index < transientResources.Length; index++)
        {
            GpuRenderGraphTransientResourcePlan resource = template.TransientResources[index];
            transientResources[index] = resource with { Resource = Map(resource.Resource) };
        }

        var transientSlots = new GpuRenderGraphTransientSlotPlan[template.TransientSlots.Count];
        for (int index = 0; index < transientSlots.Length; index++)
        {
            GpuRenderGraphTransientSlotPlan slot = template.TransientSlots[index];
            var slotResources = new GpuRenderGraphResource[slot.Resources.Count];
            for (int resourceIndex = 0; resourceIndex < slotResources.Length; resourceIndex++)
            {
                slotResources[resourceIndex] = Map(slot.Resources[resourceIndex]);
            }
            transientSlots[index] = new(
                slot.Slot,
                slot.Kind,
                slot.MemoryKind,
                slot.TextureDescription,
                slot.BufferDescription,
                slotResources);
        }

        var aliasBarriers = new GpuRenderGraphAliasBarrierPlan[template.AliasBarriers.Count];
        for (int index = 0; index < aliasBarriers.Length; index++)
        {
            GpuRenderGraphAliasBarrierPlan barrier = template.AliasBarriers[index];
            aliasBarriers[index] = new(
                barrier.DestinationPass,
                barrier.ReuseSlot,
                Map(barrier.BeforeResource),
                Map(barrier.AfterResource),
                barrier.Before,
                barrier.After,
                barrier.Hazards);
        }

        var resourceInfos = new GpuRenderGraphResourceInfo[resources.Count];
        for (int index = 0; index < resourceInfos.Length; index++)
        {
            resourceInfos[index] = resources[index].Info with { };
        }
        return new(
            resourceInfos,
            passPlans,
            barriers,
            transientResources,
            transientSlots,
            aliasBarriers,
            currentRecorders);
    }

    private int GetStructureResourceIndex(GpuRenderGraphResource resource)
    {
        if (structureResourceIndices is null || structureResourceIndices.Count != resources.Count)
        {
            var indices = new Dictionary<GpuRenderGraphResource, int>(resources.Count);
            for (int index = 0; index < resources.Count; index++)
            {
                indices.Add(resources[index].Info.Resource, index);
            }
            structureResourceIndices = indices;
        }
        return structureResourceIndices[resource];
    }

    private struct StructureHasher
    {
        private const ulong Offset = 14_695_981_039_346_656_037;
        private const ulong Prime = 1_099_511_628_211;
        private ulong value;

        public ulong Value => value == 0 ? Offset : value;

        public void Add(bool item) => Add(item ? 1ul : 0ul);
        public void Add(int item) => Add(unchecked((ulong)item));
        public void Add(uint item) => Add((ulong)item);

        public void Add(ulong item)
        {
            if (value == 0) { value = Offset; }
            for (int index = 0; index < sizeof(ulong); index++)
            {
                value = (value ^ (byte)item) * Prime;
                item >>= 8;
            }
        }

        public void Add(string item)
        {
            Add(item.Length);
            foreach (char character in item)
            {
                value = (value ^ (byte)character) * Prime;
                value = (value ^ (byte)(character >> 8)) * Prime;
            }
        }
    }
}
