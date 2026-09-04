namespace Lumyte.Graphics.RenderGraph;

/// <summary>
/// Caches immutable render-graph structure while rebinding frame callbacks and imported resources on every hit.
/// </summary>
public sealed class GpuRenderGraphPlanCache
{
    private readonly object sync = new();
    private readonly Dictionary<ulong, List<GpuRenderGraphPlanCacheEntry>> plans = [];
    private readonly Queue<GpuRenderGraphPlanCacheEntry> insertionOrder = [];
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
            if (plans.TryGetValue(hash, out List<GpuRenderGraphPlanCacheEntry>? bucket))
            {
                foreach (GpuRenderGraphPlanCacheEntry candidate in bucket)
                {
                    if (!graph.MatchesStructure(candidate.Structure)) { continue; }
                    hitCount++;
                    return graph.BindCachedPlan(candidate.Template, candidate.TemplateResourceIndices);
                }
            }

            GpuRenderGraphPlan plan = graph.CompileUncached();
            if (count == MaximumEntries)
            {
                GpuRenderGraphPlanCacheEntry evicted = insertionOrder.Dequeue();
                List<GpuRenderGraphPlanCacheEntry> evictedBucket = plans[evicted.Structure.Hash];
                evictedBucket.Remove(evicted);
                if (evictedBucket.Count == 0) { plans.Remove(evicted.Structure.Hash); }
                count--;
            }
            GpuRenderGraphPlan template = CreateTemplate(plan);
            var entry = new GpuRenderGraphPlanCacheEntry(
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

}
