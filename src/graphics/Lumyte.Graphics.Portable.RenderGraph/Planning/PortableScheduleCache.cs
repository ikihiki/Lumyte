namespace Lumyte.Graphics.Portable.RenderGraph;

internal sealed record PortableSchedule(int[] LivePasses, IReadOnlyDictionary<int, (int First, int Last)> Lifetimes);

// Cached plans contain indices only: no graph, binding snapshot, content, record callback or GPU reference is retained.
internal sealed class PortableScheduleCache(int capacity)
{
    private readonly Dictionary<string, (PortableSchedule Schedule, LinkedListNode<string> Node)> schedules = new(StringComparer.Ordinal);
    private readonly LinkedList<string> order = [];
    internal int Count => schedules.Count;
    internal long ReuseCount { get; private set; }
    internal PortableSchedule GetOrCreate(string key, Func<PortableSchedule> create)
    {
        if (schedules.TryGetValue(key, out var existing))
        { order.Remove(existing.Node); order.AddLast(existing.Node); ReuseCount++; return existing.Schedule; }
        PortableSchedule value = create();
        if (schedules.Count == capacity) { schedules.Remove(order.First!.Value); order.RemoveFirst(); }
        schedules.Add(key, (value, order.AddLast(key))); return value;
    }
}

/// <summary>CPU preparation cache observations; these values do not query GPU capabilities or command state.</summary>
public readonly record struct PortableRenderPreparationStatistics(int CachedSchedules, long ReusedSchedules);
