namespace Lumyte.Graphics.Native.RenderGraph;

public readonly record struct NativeRenderPreparationStatistics(long CacheHits, long CacheMisses, int CachedSchedules);

internal sealed record NativeSchedule(int[] Passes, (int Resource, int First, int Last)[] Lifetimes);

internal sealed class NativeScheduleCache
{
    private const int Capacity = 64;
    private readonly Dictionary<string, (NativeSchedule Schedule, LinkedListNode<string> Node)> entries = [];
    private readonly LinkedList<string> recent = [];
    private long hits, misses;
    internal NativeRenderPreparationStatistics Statistics => new(hits, misses, entries.Count);
    internal bool TryGet(string key, out NativeSchedule? schedule)
    {
        if (entries.TryGetValue(key, out var value))
        {
            hits++; recent.Remove(value.Node); recent.AddLast(value.Node); schedule = value.Schedule; return true;
        }
        misses++; schedule = null; return false;
    }
    internal void Add(string key, NativeSchedule schedule)
    {
        if (entries.Count == Capacity) { entries.Remove(recent.First!.Value); recent.RemoveFirst(); }
        entries.Add(key, (schedule, recent.AddLast(key)));
    }
    internal void Clear() { entries.Clear(); recent.Clear(); }
}
