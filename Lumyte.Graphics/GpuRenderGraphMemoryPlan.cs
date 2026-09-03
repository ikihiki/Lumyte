namespace Lumyte.Graphics;

public sealed record GpuRenderGraphPhysicalResourcePlan(
    GpuRenderGraphResource Resource,
    GpuTransientLifetime Lifetime,
    int ReuseSlot,
    ulong Size,
    ulong Alignment,
    ulong Compatibility);

public sealed class GpuRenderGraphPhysicalSlotPlan
{
    internal GpuRenderGraphPhysicalSlotPlan(
        int slot,
        GpuRenderGraphResourceKind kind,
        GpuMemoryKind memoryKind,
        ulong size,
        ulong alignment,
        ulong compatibility,
        GpuRenderGraphResource[] resources)
    {
        Slot = slot;
        Kind = kind;
        MemoryKind = memoryKind;
        Size = size;
        Alignment = alignment;
        Compatibility = compatibility;
        Resources = Array.AsReadOnly(resources);
    }

    public int Slot { get; }
    public GpuRenderGraphResourceKind Kind { get; }
    public GpuMemoryKind MemoryKind { get; }
    public ulong Size { get; }
    public ulong Alignment { get; }
    public ulong Compatibility { get; }
    public IReadOnlyList<GpuRenderGraphResource> Resources { get; }
}

/// <summary>
/// Backend-specialized physical placement for one compiled graph. Unlike logical slots, physical
/// slots may combine different descriptions when native requirements prove them compatible.
/// </summary>
public sealed class GpuRenderGraphMemoryPlan
{
    private GpuRenderGraphMemoryPlan(
        GpuRenderGraphPhysicalResourcePlan[] resources,
        GpuRenderGraphPhysicalSlotPlan[] slots,
        GpuRenderGraphAliasBarrierPlan[] aliasBarriers)
    {
        Resources = Array.AsReadOnly(resources);
        Slots = Array.AsReadOnly(slots);
        AliasBarriers = Array.AsReadOnly(aliasBarriers);
    }

    public IReadOnlyList<GpuRenderGraphPhysicalResourcePlan> Resources { get; }
    public IReadOnlyList<GpuRenderGraphPhysicalSlotPlan> Slots { get; }
    public IReadOnlyList<GpuRenderGraphAliasBarrierPlan> AliasBarriers { get; }

    internal static GpuRenderGraphMemoryPlan Create(
        GpuRenderGraphPlan plan,
        IGpuBackend backend)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(backend);
        const GpuBackendCapabilities required =
            GpuBackendCapabilities.ExplicitPlacement | GpuBackendCapabilities.MemoryAliasing;
        if ((backend.Capabilities & required) != required)
        {
            throw new NotSupportedException(
                "Backend-specialized render-graph memory plans require placed-resource aliasing.");
        }

        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceInfo> infos =
            plan.Resources.ToDictionary(info => info.Resource);
        IReadOnlyDictionary<GpuRenderGraphResource, int> declarationIndices = plan.Resources
            .Select((info, index) => (info.Resource, index))
            .ToDictionary(pair => pair.Resource, pair => pair.index);
        var candidates = new List<Candidate>(plan.TransientResources.Count);
        foreach (GpuRenderGraphTransientResourcePlan transient in plan.TransientResources)
        {
            GpuRenderGraphResourceInfo info = infos[transient.Resource];
            (ulong size, ulong alignment, ulong compatibility) = info.Kind switch
            {
                GpuRenderGraphResourceKind.Texture => TextureRequirements(backend, info),
                GpuRenderGraphResourceKind.Buffer => BufferRequirements(backend, info),
                _ => throw new InvalidOperationException("Transient resource has an unknown kind."),
            };
            candidates.Add(new(
                declarationIndices[info.Resource],
                info,
                transient.Lifetime,
                size,
                alignment,
                compatibility));
        }

        var slots = new List<MutableSlot>();
        var resourcePlans = new List<GpuRenderGraphPhysicalResourcePlan>(candidates.Count);
        foreach (Candidate candidate in candidates
            .OrderBy(candidate => candidate.Lifetime.FirstPass)
            .ThenBy(candidate => candidate.DeclarationIndex))
        {
            MutableSlot? selected = null;
            foreach (MutableSlot slot in slots)
            {
                if (!slot.TryAdd(candidate, backend)) { continue; }
                selected = slot;
                break;
            }
            if (selected is null)
            {
                selected = new(slots.Count, candidate);
                slots.Add(selected);
            }
            resourcePlans.Add(new(
                candidate.Info.Resource,
                candidate.Lifetime,
                selected.Index,
                candidate.Size,
                candidate.Alignment,
                candidate.Compatibility));
        }

        var aliasBarriers = new List<GpuRenderGraphAliasBarrierPlan>();
        foreach (MutableSlot slot in slots)
        {
            Candidate[] assigned = slot.Candidates
                .OrderBy(candidate => candidate.Lifetime.FirstPass)
                .ToArray();
            for (int index = 1; index < assigned.Length; index++)
            {
                Candidate before = assigned[index - 1];
                Candidate after = assigned[index];
                GpuRenderGraphResourceAccess beforeAccess = plan.Passes[before.Lifetime.LastPass].Accesses
                    .Single(access => access.Resource == before.Info.Resource);
                GpuRenderGraphResourceAccess afterAccess = plan.Passes[after.Lifetime.FirstPass].Accesses
                    .Single(access => access.Resource == after.Info.Resource);
                aliasBarriers.Add(new(
                    plan.Passes[after.Lifetime.FirstPass].Name,
                    slot.Index,
                    before.Info.Resource,
                    after.Info.Resource,
                    beforeAccess.Stage,
                    afterAccess.Stage,
                    beforeAccess.Hazards | afterAccess.Hazards));
            }
        }

        return new(
            [.. resourcePlans],
            [.. slots.Select(slot => slot.ToPlan())],
            [.. aliasBarriers]);
    }

    private static (ulong Size, ulong Alignment, ulong Compatibility) TextureRequirements(
        IGpuBackend backend,
        GpuRenderGraphResourceInfo info)
    {
        GpuTextureDescription description = info.TextureDescription
            ?? throw new InvalidOperationException("Transient texture has no description.");
        GpuTextureMemoryRequirements requirements =
            backend.GetTextureMemoryRequirements(description).Validate();
        return (requirements.Size, requirements.Alignment, requirements.Compatibility);
    }

    private static (ulong Size, ulong Alignment, ulong Compatibility) BufferRequirements(
        IGpuBackend backend,
        GpuRenderGraphResourceInfo info)
    {
        GpuBufferDescription description = info.BufferDescription
            ?? throw new InvalidOperationException("Transient buffer has no description.");
        GpuBufferMemoryRequirements requirements =
            backend.GetBufferMemoryRequirements(description).Validate();
        return (requirements.Size, requirements.Alignment, requirements.Compatibility);
    }

    private sealed record Candidate(
        int DeclarationIndex,
        GpuRenderGraphResourceInfo Info,
        GpuTransientLifetime Lifetime,
        ulong Size,
        ulong Alignment,
        ulong Compatibility);

    private sealed class MutableSlot
    {
        public MutableSlot(int index, Candidate first)
        {
            Index = index;
            Kind = first.Info.Kind;
            MemoryKind = first.Info.MemoryKind;
            Size = first.Size;
            Alignment = first.Alignment;
            Compatibility = first.Compatibility;
            Candidates.Add(first);
        }

        public int Index { get; }
        public GpuRenderGraphResourceKind Kind { get; }
        public GpuMemoryKind MemoryKind { get; }
        public ulong Size { get; private set; }
        public ulong Alignment { get; private set; }
        public ulong Compatibility { get; private set; }
        public List<Candidate> Candidates { get; } = [];

        public bool TryAdd(Candidate candidate, IGpuBackend backend)
        {
            if (Kind != candidate.Info.Kind
                || MemoryKind != candidate.Info.MemoryKind
                || Candidates.Any(existing => existing.Lifetime.Overlaps(candidate.Lifetime))
                || !backend.TryCombineMemoryCompatibility(
                    Compatibility,
                    candidate.Compatibility,
                    out ulong combined))
            {
                return false;
            }

            Compatibility = combined;
            Size = Math.Max(Size, candidate.Size);
            Alignment = Math.Max(Alignment, candidate.Alignment);
            Candidates.Add(candidate);
            return true;
        }

        public GpuRenderGraphPhysicalSlotPlan ToPlan() => new(
            Index,
            Kind,
            MemoryKind,
            Size,
            Alignment,
            Compatibility,
            [.. Candidates.Select(candidate => candidate.Info.Resource)]);
    }
}
