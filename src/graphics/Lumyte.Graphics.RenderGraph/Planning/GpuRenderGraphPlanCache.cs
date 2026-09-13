namespace Lumyte.Graphics.RenderGraph;

/// <summary>A bounded LRU cache of index-only logical schedules. It never owns a graph, input or GPU resource.</summary>
public sealed class GpuRenderGraphPlanCache
{
    private readonly int maximumEntries;
    private readonly object gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> recent = new();

    public GpuRenderGraphPlanCache(int maximumEntries = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        this.maximumEntries = maximumEntries;
    }
    public int Count { get { lock (gate) { return entries.Count; } } }
    public void Clear() { lock (gate) { entries.Clear(); recent.Clear(); } }

    internal int[] GetOrAdd(string topology, Func<int[]> compile)
    {
        lock (gate)
        {
            if (entries.TryGetValue(topology, out var existing))
            {
                recent.Remove(existing);
                recent.AddFirst(existing);
                return existing.Value.Passes;
            }
            var schedule = compile();
            var node = recent.AddFirst(new Entry(topology, schedule));
            entries.Add(topology, node);
            if (entries.Count > maximumEntries)
            {
                entries.Remove(recent.Last!.Value.Topology);
                recent.RemoveLast();
            }
            return schedule;
        }
    }
    private sealed record Entry(string Topology, int[] Passes);
}
