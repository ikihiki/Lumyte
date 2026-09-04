namespace Lumyte.Graphics.RenderGraph;

internal sealed class GpuRenderGraphMutableSlot
{
    public GpuRenderGraphMutableSlot(int index, GpuRenderGraphMemoryCandidate first)
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
    public List<GpuRenderGraphMemoryCandidate> Candidates { get; } = [];

    public bool TryAdd(GpuRenderGraphMemoryCandidate candidate, IGpuBackend backend)
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
